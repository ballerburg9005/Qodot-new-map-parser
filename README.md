# INFO

https://github.com/ballerburg9005/Qodot-new-map-parser/assets/50874674/66952180-de88-4860-ac85-4bc1cadffca2



https://github.com/ballerburg9005/Qodot-new-map-parser/assets/50874674/347fef18-34cf-4aa0-aa6e-3f716d18e506



I started heavy work on Qodot, so it would be able to read PatchDef2 (bezier curves used to implement basic geometry such as terrain, pillars, arches, etc.), which is required for Quake 3, Xonotic and many other open source game maps like Warsow and what not to work. There are tens of thousands of community maps for those games, and each map takes like dozens if not hundreds of hours to create, so it seemed really worthwhile to do this.

However since the implementation was quite time-consuming, in this time first there was totally unrelated drama with the devs, then Qodot was radically rewritten (ditched C# in favor of GDScript). Since everything I wrote was in C#, plus the drama, this code is basically now dead, which is pretty sad. I would like to resume this project, but it would require the Qodot devs to come forward and resolve the trust issues they created first.

*However, you can still load Xonotic and Quake 3 maps with this repository if you follow those steps.*

The same method can also be used with mainline Qodot, if you put a simple "try .. catch" inside the map parser, so it will not die when parsing patches. However, since the upstream Qodot has no patch support, the results might be too poor with the majority of maps to be playable. With this project you will get quite passable results mostly. 

However there is still a catch: Quake 3 uses .shader scripts (extensive complicated custom format), to basically create what is now known as "materials" rather than just plain (non-transparent) textures. Those .shader files create anything form sky textures and sun lighting, to 3D mesh shader wobbles of water, animated textures, transparency and a lot more. So in absence of a .shader parser, for all this eyecandy stuff to work, those things could be manually supplemented inside Godot. Just by making a skybox, maybe a generic water shader and setting transparency, again it wouldn't be that hard to make a map look passable enough for people to play and have fun.

# making it work

If you are not on Linux, use MSYS2 for the bash scripts.

1. If you only have a .bsp, use q3map2 to convert to .map like so (-game parameter is not ultimately necessary):

```
q3map2 -convert -format map -game xonotic toxic_fabric.bsp
```

2. Now in [Netradiant-custom](https://garux.github.io/NRC/), open the .map file. You might need to configure netradiant-custom for your game first.

3. Open a second window of Netradiant-custom, go to Edit->Preferences->Brush and set "New map Brush Type" to "Valve 220".

4. In the first window, go to View->Filter and check Hints and Clips and what not so it only shows solid geometry, spawn points, lights and weapons and such (for testing purposes). Now hold down CTRL + right mouse button to select the entire map and CTRL+C to copy the contents.

5. Paste the contents in the second window with the Valve 220 format.

6. Save the map like so in your new Godot game folder: toxic_fabric_valve.map

7. use this makeshift script inside your Godot folder, which tries to pull all the textures required into your Godot project and rename them inside the .map file to make sense to Godot:

   ```
   # Please change "/mnt/2/xonotic_tga/dds/textures/ /usr/share/xonotic/data/textures/" to your texture dir of the game
   mkdir textures
   MAP=toxic_fabric_valve.map; cp "$MAP" "${MAP%.*}-godot.map"; for i in `grep -osaE "[^/[:space:]]+/[^/[:space:]]+" "$MAP"  | sort -u | uniq`; do FILEN="$(echo $i | sed -En "s#.*/([^/]*)+\$#\1#gp" | sed "s#-#_#g")"; FILEN2="$(echo $i | sed "s#-#/#g" | sed -En "s#.*/([^/]*)+\$#\1#gp")"; FILEP="$(echo $i | sed "s#-#/#g" | sed -En "s#(.*)+/[^/]*\$#\1#gp")"; ACTUAL="$(find /mnt/2/xonotic_tga/dds/textures/ /usr/share/xonotic/data/textures/ -iregex ".*${FILEP}/${FILEN}\.\(tga\|png\|jpg\|jpeg\)" -or -iregex ".*${FILEP}/${FILEN2}\.\(tga\|png\|jpg\|jpeg\)" -or -iregex ".*$i\.\(tga\|png\|jpg\|jpeg\)" | head -n 1)"; if [[ "$ACTUAL" == "" ]]; then echo "Not found: ${FILEP}/${FILEN} ($i)"; else echo "Found: '$ACTUAL'"; sed -Ei "s#${i}#/${FILEP}/${FILEN}#g" "${MAP%.*}-godot.map"; mkdir -p textures/${FILEP}; convert "$ACTUAL" textures/${FILEP}/${FILEN}.png; fi; done
   ```
Please note that this will only work for basic textures, not shaders like sky or water. Though if the names in the .shader file match the .png/jpg name somewhat (often they do) then it will still show the basic texture for it. With most maps that's just fine and playable. Like you can see in the video demo, the water texture is there and it just doesn't look transparent. It is not a huge deal, since most maps use water only as decoration.

8. Download the [.NET build of Godot](https://godotengine.org/download) and use this one, instead of the regular one.

9. Clone this repo into your Godot game and then enable it in Project settings.

10. Follow the old Qodot tutorial to load your map. Basically go to Tools->C#->create C# solution, then press ALT + B, then create a QodotMap node.

11. In the inspector on the Qodot node, choose your -godot.map (e.g. toxic_fabric_valve-godot.map) as map_file and enable use_experimental_regex_parser. The parameter patch_tesselation_level controls how smooth the curves will be. Choose 4 for testing purposes.

12. Now in the 3D viewer in the top toolbar, there are (like usual with Qodot) three new buttons. Press "Full Build".

13. Your map is now imported, like usual. If a lot of brushes are missing, it means your map is not Valve 220 format.

![patches_progress_](https://github.com/ballerburg9005/Qodot-new-map-parser/assets/50874674/6a101080-58c6-470c-b130-771755cf411f)

# So I have loaded the .map, now what?

Well, you will notice obviously that there is only the geometry loaded and that you still write a lot of code to make all the features of the game work. Like jumppads, doors, spawn points, 3D models, etc.

In order to accomplish this, the easiest way is to make a post-processing @tool script that connects to the signal "build_complete()" on the QuodotMap node. Here is some stuff I started for a game project of mine (only experiment/testing) to give you some idea how it works. You will immediately see everything in the editor once you press the build button in Qodot. Note that the map was made with a custom game definition in Netradiant specifically for my game, and it's not an old community map. It uses .glTF models, which Netradiant-Custom can also understand. So you could use Netradiant-Custom for any sort of modern Godot game and do your mapping there, which doesn't require programming skills and such and is more efficient in a lot of ways. But Quake-style maps use .md3 models or other formats, such as .iqm, which need to be manually converted to .glTF for Godot to understand.

```
@tool
extends Node3D

# Called when the node enters the scene tree for the first time.
func _ready():
  # put player to spawn point (names might vary between games)
	if self.has_meta("info_player_start") and self.get_meta("info_player_start").size() > 0:
		var player_start = self.get_meta("info_player_start")[randi() % self.get_meta("info_player_start").size()]
		$Player.global_position = player_start[0]
		$Player.rotate_y(deg_to_rad(player_start[1]))
	pass

# collision masks ?
# 9 = buttons
# 10 = npcs
# 11 = vip npcs?
# 1 = rigid physics
# 2 = dynamic physics
# ???

# editor function: runs only after Qodot button is pressed
func _on_qodot_map_build_complete():
	var imported_resources = []
	self.set_meta("info_player_start", [])
	
	print("_on_qodot_map_build_complete")

  # spawn map entries, like models, etc.
	for entity in $QodotMap.get_children():
		if "properties" in entity and "classname" in entity.properties:
			var cn = entity.properties.classname
			# spawn point
			if cn.substr(0, 12) == "info_player_":
				var angle = entity.properties.angle if "angle" in entity.properties else 0
				self.get_meta("info_player_start").append([entity.global_position, float(angle)+180.0])
			# NPCs (monster_army is the name for a specific monster from Quake 1)
			elif cn == "monster_army":
					var sprite = Sprite3D.new()
					sprite.billboard = BaseMaterial3D.BILLBOARD_FIXED_Y
					sprite.offset = Vector2(0,46)
					sprite.texture_filter = 0
					# TODO: should load sprite only once, not every time sprite occurs in map and not only png
					if "sprite" in entity.properties:
						if not FileAccess.file_exists("res://sprites/"+entity.properties.sprite+".png"):
							print("ERROR: "+entity.properties.model+" does not exist!")
							continue
						sprite.texture = load("res://sprites/"+entity.properties.sprite+".png")
					else:
						sprite.texture = load("res://sprites/default_npc.png")
					
					entity.add_child(sprite)
					sprite.set_owner(get_tree().edited_scene_root)
					
					var body = AnimatableBody3D.new()
					var collision = CollisionShape3D.new()
					var shape = CapsuleShape3D.new()
					shape.height = 1.8
					shape.radius = 0.25
					collision.position += Vector3(0,0.16,0)
					collision.shape = shape
					body.set_collision_layer_value(10, true)
					body.set_collision_mask_value(32, true)
					body.add_child(collision)
					entity.add_child(body)
					body.set_owner(get_tree().edited_scene_root)
					collision.set_owner(get_tree().edited_scene_root)
			# triggers
			elif cn == "trigger_multiple":
				# get box dimensions from mesh and create an area for interaction
				for ent in entity.get_children():
					if ent.is_class("MeshInstance3D"):
						var mdt = MeshDataTool.new()
						mdt.create_from_surface(ent.mesh, 0)
						var vertecies = Array()
						for i in range(mdt.get_vertex_count()):
							vertecies.append(mdt.get_vertex(i))
						var area = Area3D.new()
						var collision = CollisionShape3D.new()
						var shape = ConvexPolygonShape3D.new()
						shape.points = vertecies
						collision.shape = shape
						area.set_collision_layer_value(9, true)  
						area.set_collision_layer_value(1, false)  
						area.set_collision_mask_value(1, false) 
						area.set_collision_mask_value(32, true) 
						
						area.add_child(collision)
						entity.add_child(area)
						area.set_owner(get_tree().edited_scene_root)
						collision.set_owner(get_tree().edited_scene_root)
			# models
			elif cn == "misc_model":
				# import model source file only if not already imported
				if not imported_resources.has("res://tmp/"+entity.properties.model+".tscn"):
					if not FileAccess.file_exists("res://"+entity.properties.model):
						print("ERROR: "+entity.properties.model+" does not exist!")
						continue
					var import = load("res://"+entity.properties.model).instantiate(1)
					# set Alpha Scissors, Pixellation and such
					var meshes = get_all_the_children(import, "MeshInstance3D")
					for mesh in meshes:
						var material = mesh.mesh.surface_get_material(0)
						material.transparency = 2
						material.specular_mode = 2
						material.diffuse_mode = 3
						material.texture_filter = 0
					# set animation
					if "animation" in entity.properties:
						var players = get_all_the_children(import, "AnimationPlayer")
						for player in players:
							var animation_name = ""
							if entity.properties.animation.to_lower() == "true":
								animation_name = player.get_animation_list()[0]
							else:
								animation_name = entity.properties.animation
							player.get_animation(animation_name).loop_mode = Animation.LOOP_LINEAR
							player.current_animation = animation_name
							player.play(animation_name)
					# save as new scene to save changes inside scene
					var dir_combined = ""
					for dir in entity.properties.model.split("/").slice(0,-1):
						var mydir = DirAccess.open("res://tmp"+dir_combined)
						mydir.make_dir(dir)
						dir_combined += "/"+dir
					var packed_scene = PackedScene.new()
					packed_scene.pack(import)
					ResourceSaver.save(packed_scene, "res://tmp/"+entity.properties.model+".tscn")
					imported_resources.append("res://tmp/"+entity.properties.model+".tscn")

				# reload saved scene from import and perform transforms
				var saved_import = load("res://tmp/"+entity.properties.model+".tscn").instantiate()
				saved_import.rotate_y(deg_to_rad(-90))
				if "modelscale_vec" in entity.properties:
					var vec = (entity.properties.modelscale_vec+" 0 0 0").split(" ")
					# not 100% accurate on y scale, no idea why ... hand measured, mystery transform!
					saved_import.scale = Vector3(abs(float(vec[0])/32.0),abs(float(vec[1])/26.0),abs(float(vec[2])/38.0))
				if "modelscale" in entity.properties:
					saved_import.scale = Vector3(abs(float(entity.properties.modelscale)/32.0),abs(float(entity.properties.modelscale)/32.0),abs(float(entity.properties.modelscale)/32.0));
				if "angle" in entity.properties:
					saved_import.rotate_y(deg_to_rad(float(entity.properties.angle)))
					
				entity.add_child(saved_import)
				saved_import.set_owner(get_tree().edited_scene_root)

func get_all_the_children(node, type = null):
	var children = []
	for n in node.get_children():
			if type == null:
				children.append(n)
			elif n.get_class() == type:
				children.append(n)
			children.append_array(get_all_the_children(n, type))
	return children

```

# further information on the .map format

Qodot can only parse the old Quake 1 map format and the Half-Life 1 Valve 220 format, which is basically just a bunch of 3D box shapes (aka brushes) with (less advanced) texture clamping plus entry definitions for like spawn points, weapons, 3D models, etc. Door triggers, invisible walls, lights, etc. are just a result of giving those two basic components custom names and properties that the game engine can make sense of, for example "model_scale" or "color" for light sources. PatchDef is basically a definition of a mesh with uv clamping that translates into a bunch of 3x3 meshes and which are made to have smooth curves based on super-custom bezier curve math. In Quake 3 you will find the new BrushDef to replace the old-style brush definitions, which uses more complicated abstract math that no one really can make sense of anymore. In Doom 3 and Quake 4 they even made this math 4-dimensional, so again the brush definitions are incompatible. But you will not find many community maps for those games. Luckily all maps can be converted to Valve 220 with Netradiant Custom, and for some oddball retro games like Heavy Metal Fuck, there is even another tool called QuArK to convert those to something Netradiant might understand. Please note that the .map format defines geometry in a highly custom manner that cannot be understood by any tool or 3D program in any way, if it was not specifically designed for this purpose. In fact it seems absolutely ridiculous nowadays how complicated the math involved is, just to save like a few MB of RAM and disk space, it caused like thousands of lines of extra code to be necessary that are not easily understood at all without an IQ of like 145 or more. Which is one of the main reasons why modern tools just don't fully support the modern variations of the .map format, and basically only Netradiant and Quark can really make sense of it.
