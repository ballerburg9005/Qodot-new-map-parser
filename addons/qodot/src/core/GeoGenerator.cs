using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace Qodot
{
	public class GeoGenerator
	{
		// Min distance between two verts in a brush before they're merged. Higher values fix angled brushes near extents.
		private const float CMP_EPSILON = 0.008f;

		private readonly Vector3 UP_VECTOR      = new Vector3(0.0f, 0.0f, 1.0f);
		private readonly Vector3 RIGHT_VECTOR   = new Vector3(0.0f, 1.0f, 0.0f);
		private readonly Vector3 FORWARD_VECTOR = new Vector3(1.0f, 0.0f, 0.0f);

		public MapData mapData;
		private int patchTesselationLevel;

		public GeoGenerator(MapData mapData, int patchTess)
		{
			this.mapData = mapData;
			this.patchTesselationLevel = patchTess>2?patchTess:2;
			
		}

		public void Run()
		{
			Span<Entity> entitySpan = mapData.GetEntitiesSpan();
			
			// Resize lists.
			mapData.entityGeo.Capacity = mapData.entities.Count;
			for (int i = 0; i < mapData.entityGeo.Capacity; i++)
			{
				mapData.entityGeo.Add(new EntityGeometry());
			}
			
			Span<EntityGeometry> entityGeoSpan = mapData.GetEntityGeoSpan();
			
			for (int e = 0; e < entitySpan.Length; e++)
			{
				entityGeoSpan[e].brushes.Capacity = entitySpan[e].brushes.Count;
				for (int i = 0; i < entityGeoSpan[e].brushes.Capacity; i++)
				{
					entityGeoSpan[e].brushes.Add(new BrushGeometry());
				}
				
				Span<Brush> brushSpan = mapData.GetBrushesSpan(e);
				Span<BrushGeometry> brushGeoSpan = mapData.GetBrushGeoSpan(e);
				for (int b = 0; b < entitySpan[e].brushes.Count; b++)
				{
					brushGeoSpan[b].faces.Capacity = brushSpan[b].faces.Count;
					for (int i = 0; i < brushGeoSpan[b].faces.Capacity; i++)
					{
						brushGeoSpan[b].faces.Add(new FaceGeometry());
					}
				}
			}
			
			// TODO: Multithread?
			GenerateAndFindCenters(0, entitySpan.Length);
			WindFaceVertices(0, entitySpan.Length);
			IndexFaceVertices(0, entitySpan.Length);
		}

		private void IndexFaceVertices(int startEntityIdx, int endEntityIdx)
		{
			for (int e = startEntityIdx; e < endEntityIdx; e++)
			{
				Span<BrushGeometry> brushGeoSpan = mapData.GetBrushGeoSpan(e);
				for (int b = 0; b < brushGeoSpan.Length; b++)
				{
					Span<FaceGeometry> faceGeoSpan = mapData.GetFaceGeoSpan(e, b);
					for (int f = 0; f < faceGeoSpan.Length; f++)
					{
						ref FaceGeometry faceGeo = ref faceGeoSpan[f];
						if (faceGeo.vertices.Count < 3) continue;
						
						faceGeo.indices.Capacity = (faceGeo.vertices.Count - 2) * 3;
						for (int i = 0; i < faceGeo.vertices.Count - 2; i++)
						{
							faceGeo.indices.Add(0);
							faceGeo.indices.Add(i + 1);
							faceGeo.indices.Add(i + 2);
						}
					}
				}
			}
		}

		private void WindFaceVertices(int startEntityIdx, int endEntityIdx)
		{
			Span<EntityGeometry> entityGeoSpan = mapData.GetEntityGeoSpan();
			for (int e = startEntityIdx; e < endEntityIdx; e++)
			{
				for (int b = 0; b < entityGeoSpan[e].brushes.Count; b++)
				{
					Span<Face> faceSpan = mapData.GetFacesSpan(e, b);
					Span<FaceGeometry> faceGeoSpan = mapData.GetFaceGeoSpan(e, b);
					for (int f = 0; f < faceSpan.Length; f++)
					{
						ref Face face = ref faceSpan[f];
						Span<FaceVertex> vertexSpan = CollectionsMarshal.AsSpan(faceGeoSpan[f].vertices);

						if (vertexSpan.Length < 3) continue;

						Vector3 windFaceBasis = (vertexSpan[1].vertex - vertexSpan[0].vertex).Normalized();
						Vector3 windFaceCenter = Vector3.Zero;
						Vector3 windFaceNormal = face.planeNormal.Normalized();

						for (int v = 0; v < vertexSpan.Length; v++)
						{
							windFaceCenter += vertexSpan[v].vertex;
						}
						windFaceCenter /= (float)vertexSpan.Length;
						
						vertexSpan.Sort((l, r) =>
						{   
							Vector3 u = windFaceBasis;
							Vector3 v = u.Cross(windFaceNormal);

							Vector3 loc_a = l.vertex - windFaceCenter;
							float a_pu = loc_a.Dot(u);
							float a_pv = loc_a.Dot(v);

							Vector3 loc_b = r.vertex - windFaceCenter;
							float b_pu = loc_b.Dot(u);
							float b_pv = loc_b.Dot(v);

							float a_angle = Mathf.Atan2(a_pv, a_pu);
							float b_angle = Mathf.Atan2(b_pv, b_pu);

							if (a_angle == b_angle) return 0;
							return a_angle > b_angle ? 1 : -1;
						});
					}
				}
			}
		}

		// Theoretically thread safe.
		private void GenerateAndFindCenters(int startEntityIdx, int endEntityIdx)
		{
			Span<Entity> entitySpan = mapData.GetEntitiesSpan();
			
			for (int e = startEntityIdx; e < endEntityIdx; e++)
			{
				ref Entity entity = ref entitySpan[e];
				entity.center = Vector3.Zero;
				
				Span<Brush> brushSpan = mapData.GetBrushesSpan(e);
				Span<BrushGeometry> brushGeoSpan = mapData.GetBrushGeoSpan(e);
				for (int b = 0; b < brushSpan.Length; b++)
				{
					ref Brush brush = ref brushSpan[b];
					brush.center = Vector3.Zero;
					int vertexCount = 0;
					
					if(brush.patch.verts == null) 
						 GenerateBrushVertices(e, b);
					else GenerateBezierVertices(this.patchTesselationLevel, e, b);
					
					Span<FaceGeometry> faceGeoSpan = mapData.GetFaceGeoSpan(e, b);
					for (int f = 0; f < faceGeoSpan.Length; f++)
					{
						Span<FaceVertex> vertexSpan = CollectionsMarshal.AsSpan(faceGeoSpan[f].vertices);
						for (int v = 0; v < vertexSpan.Length; v++)
						{
							brush.center += vertexSpan[v].vertex;
							vertexCount++;
						}
					}

					if (vertexCount > 0)
					{
						brush.center /= (float)vertexCount;
					}
					entity.center += brush.center;
				}

				if (brushSpan.Length > 0)
				{
					entity.center /= (float)brushSpan.Length;
				}
			}
		}

		private void GenerateBrushVertices(int entityIdx, int brushIdx)
		{
			Span<Entity> entities = mapData.GetEntitiesSpan();
			ref Entity entity = ref entities[entityIdx];

			Span<Brush> brushes = mapData.GetBrushesSpan(entityIdx);
			ref Brush brush = ref brushes[brushIdx];
			int faceCount = brush.faces.Count;

			Span<Face> faces = mapData.GetFacesSpan(entityIdx, brushIdx);
			Span<FaceGeometry> faceGeos = mapData.GetFaceGeoSpan(entityIdx, brushIdx);
			Span<TextureData> textures = CollectionsMarshal.AsSpan<TextureData>(mapData.textures);
			
			bool phong = entity.properties.GetValueOrDefault("_phong", "0") == "1";
			string phongAngleStr = entity.properties.GetValueOrDefault("_phong_angle", "89.0");
			float phongAngle = phongAngleStr.IsValidFloat() ? phongAngleStr.ToFloat() : 89.0f;

			for (int f0 = 0; f0 < faceCount; f0++)
			{
				ref Face face = ref faces[f0];
				ref FaceGeometry faceGeo = ref faceGeos[f0];
				ref TextureData texture = ref textures[face.textureIdx];
				
				for (int f1 = 0; f1 < faceCount; f1++)
				{
					for (int f2 = 0; f2 < faceCount; f2++)
					{
						Vector3? vertex = IntersectFaces(ref faces[f0], ref faces[f1], ref faces[f2]);
						if (vertex == null || !VertexInHull(brush.faces, vertex.Value)) continue;

						// If we already generated a vertex close enough to this one, then merge them.
						bool merged = false;
						for (int f3 = 0; f3 <= f0; f3++)
						{
							ref FaceGeometry otherFaceGeo = ref faceGeos[f3];
							for (int i = 0; i < otherFaceGeo.vertices.Count; i++)
							{
								if (otherFaceGeo.vertices[i].vertex.DistanceTo(vertex.Value) < CMP_EPSILON)
								{
									vertex = otherFaceGeo.vertices[i].vertex;
									merged = true;
									break;
								}
							}
							if (merged)
							{
								break;
							}
						}

						Vector3 normal = Vector3.Zero;
						if (phong)
						{
							float threshold = Mathf.Cos((phongAngle + 0.01f) * 0.0174533f);
							normal = face.planeNormal;
							if (face.planeNormal.Dot(faces[f1].planeNormal) > threshold)
								normal += faces[f1].planeNormal;
							if (face.planeNormal.Dot(faces[f2].planeNormal) > threshold)
								normal += faces[f2].planeNormal;
						}
						else
						{
							normal = face.planeNormal;
						}

						Vector2 uv = Vector2.Zero;
						Vector4 tangent = Vector4.Zero;

						if (face.isValveUV)
						{
							uv = GetValveUV(vertex.Value, ref face, texture.width, texture.height);
							tangent = GetValveTangent(ref face);
						}
						else
						{
							uv = GetStandardUV(vertex.Value, ref face, texture.width, texture.height);
							tangent = GetStandardTangent(ref face);
						}

						// Check for a duplicate vertex in the current face.
						int duplicateIdx = -1;
						for (int i = 0; i < faceGeo.vertices.Count; i++)
						{
							if (faceGeo.vertices[i].vertex == vertex.Value)
							{
								duplicateIdx = i;
								break;
							}
						}

						if (duplicateIdx < 0)
						{
							FaceVertex newVert = new FaceVertex();
							newVert.vertex = vertex.Value;
							newVert.normal = normal;
							newVert.tangent = tangent;
							newVert.uv = uv;
							faceGeo.vertices.Add(newVert);
						}
						else if (phong)
						{
							FaceVertex duplicate = faceGeo.vertices[duplicateIdx];
							duplicate.normal += normal;
							faceGeo.vertices[duplicateIdx] = duplicate;
						}
					}
				}
			}
			
			for (int i = 0; i < faceGeos.Length; i++)
			{
				Span<FaceVertex> verts = CollectionsMarshal.AsSpan<FaceVertex>(faceGeos[i].vertices);
				for (int j = 0; j < verts.Length; j++)
				{
					verts[j].normal = verts[j].normal.Normalized();
				}
			}
		}

		private Vector3? IntersectFaces(ref Face f0, ref Face f1, ref Face f2)
		{
			Vector3 n0 = f0.planeNormal;
			Vector3 n1 = f1.planeNormal;
			Vector3 n2 = f2.planeNormal;

			float denom = n0.Cross(n1).Dot(n2);
			if (denom < CMP_EPSILON) return null;

			return (n1.Cross(n2) * f0.planeDist + n2.Cross(n0) * f1.planeDist + n0.Cross(n1) * f2.planeDist) / denom;
		}

		private bool VertexInHull(List<Face> faces, Vector3 vertex)
		{
			for (int i = 0; i < faces.Count; i++)
			{
				float proj = faces[i].planeNormal.Dot(vertex);
				if (proj > faces[i].planeDist && Mathf.Abs(faces[i].planeDist - proj) > CMP_EPSILON) return false;
			}

			return true;
		}

		private Vector2 GetStandardUV(Vector3 vertex, ref Face face, int texW, int texH)
		{
			Vector2 uvOut = Vector2.Zero;

			float du = Mathf.Abs(face.planeNormal.Dot(UP_VECTOR));
			float dr = Mathf.Abs(face.planeNormal.Dot(RIGHT_VECTOR));
			float df = Mathf.Abs(face.planeNormal.Dot(FORWARD_VECTOR));

			if (du >= dr && du >= df)
				uvOut = new Vector2(vertex.X, -vertex.Y);
			else if (dr >= du && dr >= df)
				uvOut = new Vector2(vertex.X, -vertex.Z);
			else if (df >= du && df >= dr)
				uvOut = new Vector2(vertex.Y, -vertex.Z);

			float angle = Mathf.DegToRad(face.uvExtra.rot);
			uvOut = new Vector2(
				uvOut.X * Mathf.Cos(angle) - uvOut.Y * Mathf.Sin(angle),
				uvOut.X * Mathf.Sin(angle) + uvOut.Y * Mathf.Cos(angle));

			uvOut.X /= texW;
			uvOut.Y /= texH;

			uvOut.X /= face.uvExtra.scaleX;
			uvOut.Y /= face.uvExtra.scaleY;

			uvOut.X += face.uvStandard.X / texW;
			uvOut.Y += face.uvStandard.Y / texH;

			return uvOut;
		}

		private Vector2 GetValveUV(Vector3 vertex, ref Face face, int texW, int texH)
		{
			Vector2 uvOut = Vector2.Zero;
			Vector3 uAxis = face.uvValve.U.axis;
			Vector3 vAxis = face.uvValve.V.axis;
			float uShift = face.uvValve.U.offset;
			float vShift = face.uvValve.V.offset;
	
			uvOut.X = uAxis.Dot(vertex);
			uvOut.Y = vAxis.Dot(vertex);
	
			uvOut.X /= texW;
			uvOut.Y /= texH;
	
			uvOut.X /= face.uvExtra.scaleX;
			uvOut.Y /= face.uvExtra.scaleY;
	
			uvOut.X += uShift / texW;
			uvOut.Y += vShift / texH;

			return uvOut;
		}

		private Vector4 GetStandardTangent(ref Face face)
		{
			float du = face.planeNormal.Dot(UP_VECTOR);
			float dr = face.planeNormal.Dot(RIGHT_VECTOR);
			float df = face.planeNormal.Dot(FORWARD_VECTOR);
			float dua = Mathf.Abs(du);
			float dra = Mathf.Abs(dr);
			float dfa = Mathf.Abs(df);
	
			Vector3 uAxis = Vector3.Zero;
			float vSign = 0.0f;
	
			if (dua >= dra && dua >= dfa)
			{
				uAxis = FORWARD_VECTOR;
				vSign = Mathf.Sign(du);
			}
			else if (dra >= dua && dra >= dfa)
			{
				uAxis = FORWARD_VECTOR;
				vSign = -Mathf.Sign(dr);
			}
			else if (dfa >= dua && dfa >= dra)
			{
				uAxis = RIGHT_VECTOR;
				vSign = Mathf.Sign(df);
			}
			
			vSign *= Mathf.Sign(face.uvExtra.scaleY);
			uAxis = uAxis.Rotated(face.planeNormal, Mathf.DegToRad(-face.uvExtra.rot) * vSign);

			return new Vector4(uAxis.X, uAxis.Y, uAxis.Z, vSign);
		}

		private Vector4 GetValveTangent(ref Face face)
		{
			Vector3 uAxis = face.uvValve.U.axis.Normalized();
			Vector3 vAxis = face.uvValve.V.axis.Normalized();
			float vSign = -Mathf.Sign(face.planeNormal.Cross(uAxis).Dot(vAxis));

			return new Vector4(uAxis.X, uAxis.Y, uAxis.Z, vSign);
		}

		private int GetBrushVertexCount(int entityIdx, int brushIdx)
		{
			int count = 0;
			BrushGeometry brushGeo = mapData.entityGeo[entityIdx].brushes[brushIdx];
			for (int i = 0; i < brushGeo.faces.Count; i++)
			{
				count += brushGeo.faces[i].vertices.Count;
			}

			return count;
		}

		private int GetBrushIndexCount(int entityIdx, int brushIdx)
		{
			int count = 0;
			BrushGeometry brushGeo = mapData.entityGeo[entityIdx].brushes[brushIdx];
			for (int i = 0; i < brushGeo.faces.Count; i++)
			{
				count += brushGeo.faces[i].indices.Count;
			}

			return count;
		}
		
		// Calculate UVs for our tessellated vertices 
		private Vector2 BezCurveUV(float t, Vector2 p0, Vector2 p1, Vector2 p2)
		{
			Vector2 bezPoint;

			float a = 1f - t;
			float tt = t * t;

			float[] tPoints = new float[2];
			for (int i = 0; i < 2; i++)
			{
				tPoints [i] = ((a * a) * p0 [i]) + (2 * a) * (t * p1 [i]) + (tt * p2 [i]);
			}

			bezPoint = new Vector2(tPoints [0], tPoints [1]);

			return bezPoint;
		}

		// Calculate a vector3 at point t on a bezier curve between
		// p0 and p2 via p1.  
		private Vector3 BezCurve(float t, Vector3 p0, Vector3 p1, Vector3 p2)
		{
			Vector3 bezPoint;

			float a = 1f - t;
			float tt = t * t;

			float[] tPoints = new float[3];
			for (int i = 0; i < 3; i++)
			{
				tPoints [i] = ((a * a) * p0 [i]) + (2 * a) * (t * p1 [i]) + (tt * p2 [i]);
			}

			bezPoint = new Vector3(tPoints [0], tPoints [1], tPoints [2]);

			return bezPoint;
		}

		// This takes a tessellation level and three vector3
		// p0 is start, p1 is the midpoint, p2 is the endpoint
		// The returned list begins with p0, ends with p2, with
		// the tessellated verts in between.
		private List<Vector3> Tessellate(int level, Vector3 p0, Vector3 p1, Vector3 p2)
		{
			List<Vector3> vects = new List<Vector3>();

			float stepDelta = 1.0f / level;
			float step = stepDelta;

			vects.Add(p0);
			for (int i = 0; i < (level - 1); i++)
			{
				vects.Add(BezCurve(step, p0, p1, p2));
				step += stepDelta;
			}
			vects.Add(p2);
			return vects;
		}

		// Same as above, but for UVs
		private List<Vector2> TessellateUV(int level, Vector2 p0, Vector2 p1, Vector2 p2)
		{
			List<Vector2> vects = new List<Vector2>();

			float stepDelta = 1.0f / level;
			float step = stepDelta;

			vects.Add(p0);
			for (int i = 0; i < (level - 1); i++)
			{
				vects.Add(BezCurveUV(step, p0, p1, p2));
				step += stepDelta;
			}
			vects.Add(p2);
			return vects;
		}
		private bool GenerateBezierVertices(int level, int entityIdx, int brushIdx)
		{
			
			Span<FaceGeometry> faceGeos;
			Span<Brush> brushes = mapData.GetBrushesSpan(entityIdx);
			
			ref Brush brush = ref brushes[brushIdx];		

			Span<BrushGeometry> brushGeoSpan = mapData.GetBrushGeoSpan(entityIdx);
			
			PatchData section;

			int counter = 0;
			for (int g = 0; g < brush.patch.rows; g+=2)
			{
				if(g+2 >= brush.patch.rows) continue;
				for (int h = 0; h < brush.patch.cols; h+=2)
				{
					if(h+2 >= brush.patch.cols) continue;

					section = GenerateBezierVerticesSub(level, entityIdx, brushIdx, g, h);
				
					// TODO: allocate all at once ... though its somewhat of a mystery how many verts it returns
					brushGeoSpan[brushIdx].faces.Capacity = brushGeoSpan[brushIdx].faces.Capacity+section.index.Count/3;
					for (int i = 0; i < section.index.Count/3; i++)
						brushGeoSpan[brushIdx].faces.Add(new FaceGeometry());
						
					faceGeos = mapData.GetFaceGeoSpan(entityIdx, brushIdx);
				
					for (int i = section.index.Count-1; i >= 0; i--)
					{
						ref FaceGeometry faceGeo = ref faceGeos[counter/3]; // TODO, is it really smart to create faces, or do we need seperate pipeline?
						FaceVertex newVert = new FaceVertex();
						newVert.vertex = section.verts[section.index[i]];
						Vector3 vectorA, vectorB;

						if(i-2 < 0) // TODO, normal code is shit? What about first 3 vertices?
						{
							vectorA = section.verts[section.index[2]] - section.verts[section.index[0]];
							vectorB = section.verts[section.index[1]] - section.verts[section.index[0]];
						}
						else
						{
							vectorA = section.verts[section.index[i-1]] - section.verts[section.index[i-0]];
							vectorB = section.verts[section.index[i-2]] - section.verts[section.index[i-0]];
						}
						
						newVert.normal = new Vector3(0,0,0); //vectorA.Cross(vectorB).Normalized();


						newVert.tangent = new Vector4(0,0,0,0);
						newVert.uv =  section.uvs[section.index[i]];
						faceGeo.vertices.Add(newVert);
						
						counter++;
					}
				}
			}
			return true;
		}
		// Where the magic happens. This function computes each 3x3 segment of a patch individually. 
		// Credit: uQuake https://github.com/mikezila/uQuake3
		private PatchData GenerateBezierVerticesSub(int level, int entityIdx, int brushIdx, int g, int h)
		{
			Span<Brush> brushes = mapData.GetBrushesSpan(entityIdx);
			
			ref Brush brush = ref brushes[brushIdx];
			
			// We'll use these two to hold our verts, tris, and uvs
			List<Vector3> vertex = new List<Vector3>();
			List<int> index = new List<int>();
			List<Vector2> uvs = new List<Vector2>();
			List<Vector2> uv2s = new List<Vector2>();

	
			List<Vector3> control = new List<Vector3>();
			control.AddRange(brush.patch.verts.GetRange(brush.patch.cols*g+h, 3));
			control.AddRange(brush.patch.verts.GetRange(brush.patch.cols*g+brush.patch.cols+h, 3));
			control.AddRange(brush.patch.verts.GetRange(brush.patch.cols*g+brush.patch.cols*2+h, 3));
			List<Vector2> controlUvs = new List<Vector2>();
			controlUvs.AddRange(brush.patch.uvs.GetRange(brush.patch.cols*g+h, 3));
			controlUvs.AddRange(brush.patch.uvs.GetRange(brush.patch.cols*g+brush.patch.cols+h, 3));
			controlUvs.AddRange(brush.patch.uvs.GetRange(brush.patch.cols*g+brush.patch.cols*2+h, 3));
			
			// The incoming list is 9 entires, 
			// referenced as p0 through p8 here.

			// Generate extra rows to tessellate
			// each row is three control points
			// start, curve, end
			// The "lines" go as such
			// p0s from p0 to p3 to p6 ''
			// p1s from p1 p4 p7
			// p2s from p2 p5 p8

			List<Vector2> p0suv;
			List<Vector3> p0s;
			p0s = Tessellate(level, control [0], control [3], control [6]);
			p0suv = TessellateUV(level, controlUvs [0], controlUvs [3], controlUvs [6]);

			List<Vector2> p1suv;
			List<Vector3> p1s;
			p1s = Tessellate(level, control [1], control [4], control [7]);
			p1suv = TessellateUV(level, controlUvs [1], controlUvs [4], controlUvs [7]);

			List<Vector2> p2suv;
			List<Vector3> p2s;
			p2s = Tessellate(level, control [2], control [5], control [8]);
			p2suv = TessellateUV(level, controlUvs [2], controlUvs [5], controlUvs [8]);

			// Tessellate all those new sets of control points and pack
			// all the results into our vertex array, which we'll return.
			// Make our uvs list while we're at it.
			for (int i = 0; i <= level; i++)
			{
				vertex.AddRange(Tessellate(level, p0s [i], p1s [i], p2s [i]));
				uvs.AddRange(TessellateUV(level, p0suv [i], p1suv [i], p2suv [i]));
			}

			// This will produce (tessellationLevel + 1)^2 verts
			int numVerts = (level + 1) * (level + 1);

			// Computer triangle indexes for forming a mesh.
			// The mesh will be tessellationlevel + 1 verts
			// wide and tall.
			int xStep = 1;
			int width = level + 1;
			for (int i = 0; i < numVerts - width; i++)
			{
				//on left edge
				if (xStep == 1)
				{
					index.Add(i);
					index.Add(i + width);
					index.Add(i + 1);

					xStep++;
					continue;
				} else if (xStep == width) //on right edge
				{
					index.Add(i);
					index.Add(i + (width - 1));
					index.Add(i + width);

					xStep = 1;
					continue;
				} else // not on an edge, so add two
				{
					index.Add(i);
					index.Add(i + (width - 1));
					index.Add(i + width);


					index.Add(i);
					index.Add(i + width);
					index.Add(i + 1);

					xStep++;
					continue;
				}
			}

			PatchData val = new PatchData();
			val.index = index;
			val.verts = vertex;
			val.uvs   = uvs;
			
			return val;
		}

	}
}
