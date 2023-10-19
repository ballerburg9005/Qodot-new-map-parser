using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using System.Reflection;
using System.Text;

namespace Qodot
{
public class MapParserRegex
{
	public MapData mapData;


	public MapParserRegex(MapData mapData)
	{
		this.mapData = mapData;
	}


	public bool Load(string filename)
	{
		// {} -> (), F = texture, x = number, N = newline, Q = optional newline, O = .*, Y = "(//|;) TX"
		string parserBrushPattern		= simpPatternTranslate("{(x+)}+{({(x+)}+)}* F {[x+]}*x*{Yx}*", false); 
		string parserPatchPattern 		= simpPatternTranslate("FN{(x+)}*Q(O)", false); 
		string parserPatchSubPattern 	= simpPatternTranslate("(x+)", true);
		
		GD.PrintErr("DEBUG: Regex parser enabled.");
	
		StringBuilder parserRawString = new StringBuilder();

		using FileAccess file = FileAccess.Open(filename, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr("Error: Failed to open map file (" + filename + ")");
			return false;
		}

		while (!file.EofReached())
			parserRawString.Append(file.GetLine()+"\n");
	
		// note: to be confusing, the "raw" entries contain extraneous information from the outer nesting in the "before" key
		List<Dictionary<string, string>> parserRawEntities = bracketParser(parserRawString.ToString());
		foreach(Dictionary<string, string> parserRawEntity in parserRawEntities)
		{
			if(parserRawEntity.ContainsKey("brackets"))
			{
				Entity								currentEntity		= new Entity();
				List<Dictionary<string, string>>	parserRawGeoItems 	= bracketParser(parserRawEntity["brackets"]);
				string								parserEntityHint  	= extractItemHint(parserRawEntity["before"]);
				Dictionary<string, string>			entityProperties  	= propertyParser(parserRawGeoItems[0]["before"]);

				// add properties
				foreach(KeyValuePair<string, string> currentPair in entityProperties)
					if (!currentEntity.properties.ContainsKey(currentPair.Key)) currentEntity.properties.Add(currentPair.Key, currentPair.Value);	
	
				// add geometry items (brushes and patches)
				foreach(Dictionary<string, string> parserRawGeoItem in parserRawGeoItems)
				{
					Brush currentBrush              = new Brush();
					string	parserGeoItemHint       = extractItemHint(parserRawGeoItem["before"]);
	
					if(parserRawGeoItem.ContainsKey("brackets"))
					{
						List<Dictionary<string, string>> parserRawGeoItemSubs = bracketParser(parserRawGeoItem["brackets"]);
	
						string parserInput = parserRawGeoItemSubs[0]["before"];
						// extract patchDef and brushDef information and switch input if successful
						string parserGeoDefName = Regex.Match(parserRawGeoItemSubs[0]["before"].Split('\n').FirstOrDefault().ToLower(), @"^[\t ]*((?:patch|brush)def[0-9]*)[\t ]*$").Groups[1].Value;
						if(parserGeoDefName.Length > 0)
							parserInput = bracketParser(parserRawGeoItemSubs[0]["brackets"])[0]["before"];
	
						// handle Patches
						if(Regex.Match(parserGeoDefName, @"patch").Success)
						{
							string parserPreparedInput  = removeComments(parserInput).Replace("\n", " \n").Replace(")", " )");
							Match parserRegexResult     = Regex.Match(parserPreparedInput, parserPatchPattern, RegexOptions.Singleline);
								
							Console.WriteLine(parserPatchPattern);
							if(parserRegexResult.Success)
							{
								Console.WriteLine("Got Texture: "+parserRegexResult.Groups[1].Value);
								Console.WriteLine("Got Texture Settings: "+GetCaptures(parserRegexResult.Groups[2]));
							
									
								string parserPreparedInputSub = removeComments(parserRegexResult.Groups[3].Value).Replace("\n", " \n").Replace(")", " )");
								
								List<Vector3> control = new List<Vector3>();
								List<Vector2> controlUVs = new List<Vector2>();
								
								int rows = 0;
								int cols = 0;
								
								foreach(string parserPreparedInputSubLine in parserPreparedInputSub.Split("\n"))
								{
									MatchCollection parserRegexResultSub = Regex.Matches(parserPreparedInputSubLine, parserPatchSubPattern);
									
									if(parserRegexResultSub.Count > 0) 
									{
										rows++;
										cols = 0;
									}
									
									foreach(Match parserRegexMatch in parserRegexResultSub)
									{
										Console.WriteLine("Got patch vector & UV: "+GetCaptures(parserRegexMatch.Groups[1]));
										if(parserRegexMatch.Groups[1].Captures.Count == 5)			// patchDef2
										{
											control.Add(new Vector3(toFloat(parserRegexMatch.Groups[1].Captures[0].Value), toFloat(parserRegexMatch.Groups[1].Captures[1].Value), toFloat(parserRegexMatch.Groups[1].Captures[2].Value)));
											controlUVs.Add(new Vector2(toFloat(parserRegexMatch.Groups[1].Captures[3].Value), toFloat(parserRegexMatch.Groups[1].Captures[4].Value)));
											cols++;
										}
										else Console.WriteLine("Error, patch unexpected vertex count"); // TODO handle this
									}
								}
								currentBrush.patch = new Patch();
								currentBrush.patch.verts = control;
								currentBrush.patch.uvs = controlUVs;
								currentBrush.patch.rows = rows;
								currentBrush.patch.cols = cols;
								currentBrush.patch.textureIdx = mapData.RegisterTexture(stripQuotes(parserRegexResult.Groups[1].Value.Replace(" ", "").Replace("\t ", "")));
								
								if(parserRegexResult.Groups[2].Captures.Count >= 5) // TODO this is nonsense, values (3 3 0 0) never used?!
								{
									currentBrush.patch.uvStandard = new Vector2(toFloat(parserRegexResult.Groups[2].Captures[0].Value),toFloat(parserRegexResult.Groups[2].Captures[1].Value));
									currentBrush.patch.uvExtra.rot = toFloat(parserRegexResult.Groups[2].Captures[2].Value);
									currentBrush.patch.uvExtra.scaleX = toFloat(parserRegexResult.Groups[2].Captures[3].Value);
									currentBrush.patch.uvExtra.scaleY = toFloat(parserRegexResult.Groups[2].Captures[4].Value);
								}
								else Console.WriteLine("Error, patch unexpected texture parameter count"); // TODO handle this
							}
							else
							{
								Console.WriteLine("Error in "+parserEntityHint+" ("+parserGeoDefName+") in "+parserGeoItemHint+": patchdef doesn't compute.");
							}
							
							
						}
						// handle Brushes
						else
						{
							Console.WriteLine("Currently (geodef)'"+parserGeoDefName+"' in "+parserEntityHint+": "+parserGeoItemHint);
	
							string parserPreparedInput 			= removeComments(parserInput).Replace("\n", " \n").Replace(")", " )").Replace("]", " ]");
							MatchCollection parserRegexResult 	= Regex.Matches(parserPreparedInput, parserBrushPattern, RegexOptions.Singleline);
							foreach(Match parserRegexMatch in parserRegexResult)
							{	
								Face currentFace   = new Face();

								// Group 1 : face plane // Group 2: texture matrix (brushdef) // Group 3: texture file // Group 4: valve UV // Group 5: Quake 1 classic texture manipulators // Group 6: TX comment number (weird old Q1 variation)
	
								// face
								if(parserRegexMatch.Groups[1].Captures.Count == 12)		// brushDef3
								{
									Console.WriteLine("Got 3x4 face plane: "+GetCaptures(parserRegexMatch.Groups[1]));
								}
								else if(parserRegexMatch.Groups[1].Captures.Count == 9)		// brushDef & classic
								{
									Console.WriteLine("Got 3x3 face plane: "+GetCaptures(parserRegexMatch.Groups[1]));
				
									currentFace.planePoints.v0 = new Vector3(toFloat(parserRegexMatch.Groups[1].Captures[0].Value), toFloat(parserRegexMatch.Groups[1].Captures[1].Value), toFloat(parserRegexMatch.Groups[1].Captures[2].Value));
									currentFace.planePoints.v1 = new Vector3(toFloat(parserRegexMatch.Groups[1].Captures[3].Value), toFloat(parserRegexMatch.Groups[1].Captures[4].Value), toFloat(parserRegexMatch.Groups[1].Captures[5].Value));
									currentFace.planePoints.v2 = new Vector3(toFloat(parserRegexMatch.Groups[1].Captures[6].Value), toFloat(parserRegexMatch.Groups[1].Captures[7].Value), toFloat(parserRegexMatch.Groups[1].Captures[8].Value));
								}
								else
								{
									Console.WriteLine("Error in "+parserEntityHint+" ("+parserGeoDefName+") in "+parserGeoItemHint+": unexpected vector component sum = "+parserRegexMatch.Groups[1].Captures.Count);
								}
								// texture plane
								if(parserRegexMatch.Groups[2].Captures.Count == 6)		// brushDef2 & brushDef3
								{
									Console.WriteLine("Got 2x3 texture vector + UV: "+GetCaptures(parserRegexMatch.Groups[2]));
								}
								else if(parserRegexMatch.Groups[2].Captures.Count == 0)
								{
									Console.WriteLine("Got no texture plane");
								}
								
								// texture
								Console.WriteLine("Got Texture: "+parserRegexMatch.Groups[3].Value);
	
								// Valve UV
								if(parserRegexMatch.Groups[4].Captures.Count >= 8)
								{
									Console.WriteLine("Got valve UV: "+GetCaptures(parserRegexMatch.Groups[4]));

									currentFace.uvValve.U.axis 	= new Vector3(toFloat(parserRegexMatch.Groups[4].Captures[0].Value), toFloat(parserRegexMatch.Groups[4].Captures[1].Value), toFloat(parserRegexMatch.Groups[4].Captures[2].Value));
									currentFace.uvValve.U.offset 	= toFloat(parserRegexMatch.Groups[4].Captures[3].Value);
									currentFace.uvValve.V.axis 	= new Vector3(toFloat(parserRegexMatch.Groups[4].Captures[4].Value), toFloat(parserRegexMatch.Groups[4].Captures[5].Value), toFloat(parserRegexMatch.Groups[4].Captures[6].Value));
									currentFace.uvValve.V.offset 	= toFloat(parserRegexMatch.Groups[4].Captures[7].Value);
									currentFace.isValveUV 		= true;	
								}
								// (texture) manipulators at the end
								List<string> appendix = new List<string>();
								foreach(Capture appendixCapture in parserRegexMatch.Groups[5].Captures)
									appendix.Add(appendixCapture.Value);
								// append fake zeros in case of malformation or unexpected data
								for (int i = 0; i <= 4; i++)
										appendix.Add("0");
									
								if(appendix.Count > 0)
								{
									Console.WriteLine("Got classic manipulators: "+GetCaptures(parserRegexMatch.Groups[5]));

									// Valve 220
									if(!currentFace.isValveUV)
										currentFace.uvStandard = new Vector2(toFloat(appendix[0]),toFloat(appendix[1]));
									// brushdef
									else if (parserRegexMatch.Groups[2].Captures.Count > 0) {}
										// TODO !
									// normal
									else
									currentFace.uvExtra.rot 	= toFloat(appendix[currentFace.isValveUV?0:2]);
									currentFace.uvExtra.scaleX	= toFloat(appendix[currentFace.isValveUV?1:3]);
									currentFace.uvExtra.scaleY	= toFloat(appendix[currentFace.isValveUV?2:4]);

								}
								// TX comment (?)
								if(parserRegexMatch.Groups[6].Captures.Count > 0)
								{
									Console.WriteLine("Got TX comment, whatever that is: "+GetCaptures(parserRegexMatch.Groups[6]));
								}

								Vector3 v0v1 = currentFace.planePoints.v1 - currentFace.planePoints.v0;
								Vector3 v1v2 = currentFace.planePoints.v2 - currentFace.planePoints.v1;
								currentFace.planeNormal = v1v2.Cross(v0v1).Normalized();
								currentFace.planeDist = currentFace.planeNormal.Dot(currentFace.planePoints.v0);
								currentFace.textureIdx = mapData.RegisterTexture(stripQuotes(parserRegexMatch.Groups[3].Value.Replace(" ", "").Replace("\t ", "")));
			
								currentBrush.faces.Add(currentFace);
							}
						}
						if(currentBrush.faces.Count > 0 || currentBrush.patch.verts != null)
							currentEntity.brushes.Add(currentBrush);
						else
							GD.Print("Empty brush: "+parserGeoItemHint);
					}
					
				}
				currentEntity.spawnType = EntitySpawnType.ENTITY;
				mapData.entities.Add(currentEntity);
			}
			else  Console.WriteLine("Map error, no content.");
			
		}
		
		GD.Print("Map parsed: entities " + mapData.entities.Count.ToString());
		
		return true;
	}


	// toFloat helper function
	private static float toFloat(string input)
	{
		return((float)Convert.ToDouble(input, System.Globalization.CultureInfo.InvariantCulture.NumberFormat));
	}


	// debug helper function
	private static string GetCaptures(Group group)
	{
		string[] captures = new string[group.Captures.Count];
		for (int i = 0; i < group.Captures.Count; i++)
		{
			captures[i] = group.Captures[i].Value;
		}
		return string.Join(", ", captures);
	}


	// translates easy to read pattern to real regex, e.g. "(x x)" -> "\([ \t]*(-?\d+(?:\.\d+)?)[ \t]*[ \t][ \t]*(-?\d+(?:\.\d+)?)[ \t]*\)"
	private string simpPatternTranslate(string input, bool singleLine)
	{
		string n = @"-?\d+(?:\.\d+)?";		// numbers
		string s = @"[ \t]"; 			// spaces

		string pattern = input;

		pattern = pattern.Replace(@" ", "Z");
		pattern = pattern.Replace(@"[", "A");
		pattern = pattern.Replace(@"]", "B");
		pattern = Regex.Replace(pattern, @"[^{}*?+.Fx ]{1}", "$&"+@s+"*");		// insert optional spaces everywhere
		pattern = pattern.Replace(@"Z", @s);
		pattern = pattern.Replace(@"(", @"\(");	
		pattern = pattern.Replace(@")", @"\)");
		pattern = pattern.Replace(@"{", @"(?:");					// optional non-capturing groups
		pattern = pattern.Replace(@"}", @")");
		pattern = pattern.Replace(@"A", @"\[");				
		pattern = pattern.Replace(@"B", @"\]");	
		pattern = pattern.Replace(@"N", @"\n");	
		pattern = pattern.Replace(@"Q", @"\n*");	
		pattern = pattern.Replace(@"O", @"(.*)");	
		pattern = pattern.Replace(@"x", "(?:("+@n+")"+@s+"+)");				// the meat
		pattern = pattern.Replace(@"F", @s+@"*([^\n\t ]+)"+@s+"*");			// texture meat
		pattern = pattern.Replace(@"Y", @"(?://|;)"+@s+"*TX");
		
		if(!singleLine)
			pattern = pattern+@"\n";

		return(pattern);	
	}


	// [{"before": '"class" "worldspawn" ...', "brackets": '( 240 352 208 ) ( 240 272 208 ) ( 144 352 208 ) ...'}, "before": '// brush 0', '( 123 ...']
	List<Dictionary<string, string>> bracketParser(string input)
	{
		List<Dictionary<string, string>> rawItems = new List<Dictionary<string, string>>(1000);
		StringBuilder bracket    = new StringBuilder();
		StringBuilder beforeItem = new StringBuilder();
		int bracketCount = 0;

		foreach (string line in input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string linenw = line.Replace(" ", "").Replace("\t", "");
			if(linenw == "}")		bracketCount -= 1;
			if(linenw == "{")		bracketCount += 1;

			if(bracketCount == 0 && !(bracketCount == 0 && linenw == "}")) 	beforeItem.Append(line+"\n");	
			if(bracketCount >  0 && !(bracketCount == 1 && linenw == "{"))	bracket.Append(line+"\n");

			if(bracketCount == 0 && linenw == "}")
				rawItems[^1]["brackets"] = bracket.ToString();
			if(bracketCount == 1 && linenw == "{")
			{ 
				bracket = new StringBuilder();
				rawItems.Add(new Dictionary<string, string> {{"brackets", ""}, {"before", beforeItem.ToString()}});
			}
			if(bracketCount == 0 && linenw == "}")
				beforeItem = new StringBuilder();

		}
		if(rawItems.Count == 0) rawItems.Add(new Dictionary<string, string> {{"before", beforeItem.ToString()}});

		return(rawItems);	
	}

	// entity properties, bracketParser()["before"] -> {"class", "worldspawn"}
	Dictionary<string, string> propertyParser(string before)
	{
		Dictionary<string, string> properties = new Dictionary<string, string>();
		foreach(Match match in Regex.Matches(removeComments(before), @"""([^\n]*)""[ \t]""([^\n]*)""[ \t]*\n", RegexOptions.Singleline))
		{
			properties[match.Groups[1].Value] = match.Groups[2].Value;
		}
		return(properties);
	}


	// removes comments
	string removeComments(string before)
	{
		return(Regex.Replace(before, @"[\t ]*(?://|;)[^\n]*\n", "\n", RegexOptions.Singleline));
	}


	// for debugging "// brush 0" -> "brush 0"
	string extractItemHint(string before)
	{
		string output = "";
		foreach(Capture comment in Regex.Match(before, @"(?:[\t ]*(?://|;)[\t ]*([^\n]*)\n){0,2}$", RegexOptions.Singleline).Groups[1].Captures)
			output += comment.Value + ", ";
		return(Regex.Replace(output, @", $", ""));
	}
	
	// strips Quotes
	string stripQuotes(string input)
	{
		return(Regex.Match(input, @"(?:""|')*(.*)(?:""|')*").Groups[1].Value);
	}

}

}


