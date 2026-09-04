extends SceneTree

const MAIN_SCENE := preload("res://scenes/main.tscn")
const MOVE_ACTION := &"move_forward"
const MOVEMENT_ACTIONS := [&"move_left", &"move_right", &"move_forward", &"move_backward"]
const MOVEMENT_EPSILON := 0.01
const ZOOM_EPSILON := 0.001
const JOYSTICK_EPSILON := 0.01


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	# A stale editor InputMap was the source of per-frame missing-action errors.
	# The native player must reload them from project settings before reading input.
	for action in MOVEMENT_ACTIONS:
		if InputMap.has_action(action):
			InputMap.erase_action(action)

	var main := MAIN_SCENE.instantiate()
	root.add_child.call_deferred(main)
	await process_frame
	await physics_frame

	var players := get_nodes_in_group(&"player")
	if players.size() != 1:
		await _finish(main, 1, "Expected exactly one spawned player, found %d." % players.size())
		return

	var player := players[0] as CharacterBody3D
	if player == null or player.get_class() != &"ProjectFPlayer":
		await _finish(main, 1, "Spawned node is not a ProjectFPlayer CharacterBody3D.")
		return

	for action in MOVEMENT_ACTIONS:
		if not InputMap.has_action(action):
			await _finish(main, 1, "ProjectFPlayer did not restore input action %s." % action)
			return

	var joystick = player.get_node_or_null("HUD/VirtualJoystick")
	if joystick == null or joystick.get_class() != &"VirtualJoystick":
		await _finish(main, 1, "Player HUD is missing the native VirtualJoystick.")
		return
	if joystick.call("get_input_direction") != Vector2.ZERO:
		await _finish(main, 1, "VirtualJoystick must start with zero input.")
		return

	var joystick_background := joystick.get_node_or_null("Background") as TextureRect
	var joystick_handle := joystick.get_node_or_null("Handle") as TextureRect
	if joystick_background == null or joystick_background.size != Vector2(200.0, 200.0):
		await _finish(main, 1, "Joystick background must preserve the Unity 200x200 size.")
		return
	if joystick_handle == null or joystick_handle.size != Vector2(80.0, 80.0):
		await _finish(main, 1, "Joystick handle must preserve the Unity 80x80 size.")
		return
	if joystick_background.visible or joystick_handle.visible:
		await _finish(main, 1, "Floating joystick visuals must be hidden while idle.")
		return

	var joystick_press := InputEventMouseButton.new()
	joystick_press.button_index = MOUSE_BUTTON_LEFT
	joystick_press.pressed = true
	joystick_press.position = Vector2(320.0, 360.0)
	Input.parse_input_event(joystick_press)
	await process_frame
	if not joystick_background.visible or not joystick_handle.visible:
		await _finish(main, 1, "Mouse press did not activate the floating joystick.")
		return

	var joystick_drag := InputEventMouseMotion.new()
	joystick_drag.button_mask = MOUSE_BUTTON_MASK_LEFT
	joystick_drag.position = Vector2(370.0, 360.0)
	Input.parse_input_event(joystick_drag)
	await process_frame
	var joystick_direction: Vector2 = joystick.call("get_input_direction")
	if joystick_direction.x <= JOYSTICK_EPSILON or absf(joystick_direction.y) > JOYSTICK_EPSILON:
		await _finish(main, 1, "Mouse drag did not produce the expected joystick direction.")
		return

	var joystick_release := InputEventMouseButton.new()
	joystick_release.button_index = MOUSE_BUTTON_LEFT
	joystick_release.pressed = false
	joystick_release.position = joystick_drag.position
	Input.parse_input_event(joystick_release)
	await process_frame
	if joystick.call("get_input_direction") != Vector2.ZERO:
		await _finish(main, 1, "Mouse release did not reset the joystick direction.")
		return
	if joystick_background.visible or joystick_handle.visible:
		await _finish(main, 1, "Mouse release did not hide the floating joystick.")
		return

	var camera := player.get_node_or_null("Camera3D") as Camera3D
	var zoom_in_button := player.get_node_or_null("HUD/ZoomControls/ZoomIn") as Button
	var zoom_out_button := player.get_node_or_null("HUD/ZoomControls/ZoomOut") as Button
	if camera == null or zoom_in_button == null or zoom_out_button == null:
		await _finish(main, 1, "Player camera zoom UI is incomplete.")
		return
	if camera.projection != Camera3D.PROJECTION_ORTHOGONAL or absf(camera.size - 3.0) > ZOOM_EPSILON:
		await _finish(main, 1, "Player camera must start at Unity orthographic size 3.")
		return

	zoom_out_button.pressed.emit()
	if absf(float(player.call("get_target_orthographic_size")) - 3.5) > ZOOM_EPSILON:
		await _finish(main, 1, "Zoom-out button is not connected to the native zoom target.")
		return
	zoom_in_button.pressed.emit()
	if absf(float(player.call("get_target_orthographic_size")) - 3.0) > ZOOM_EPSILON:
		await _finish(main, 1, "Zoom-in button is not connected or minimum zoom is incorrect.")
		return
	for _step in 20:
		player.call("zoom_out")
	if absf(float(player.call("get_target_orthographic_size")) - 10.0) > ZOOM_EPSILON:
		await _finish(main, 1, "Camera zoom did not clamp to the Unity maximum size 10.")
		return

	var start_position := player.global_position
	Input.action_press(MOVE_ACTION)
	for frame in 6:
		await physics_frame
	Input.action_release(MOVE_ACTION)

	var horizontal_distance := Vector2(
		player.global_position.x - start_position.x,
		player.global_position.z - start_position.z
	).length()
	if horizontal_distance <= MOVEMENT_EPSILON:
		await _finish(main, 1, "Player did not move after move_forward input.")
		return

	await _finish(main, 0, "PLAYER_INPUT_UI_ZOOM_TEST_OK distance=%.4f" % horizontal_distance)


func _finish(main: Node, exit_code: int, message: String) -> void:
	Input.action_release(MOVE_ACTION)
	if exit_code == 0:
		print(message)
	else:
		push_error(message)
	main.queue_free()
	await process_frame
	quit(exit_code)
