extends SceneTree

var failures: Array[String] = []


func _init() -> void:
	var scene := load("res://scenes/main.tscn") as PackedScene
	_check(scene != null, "main scene loads")
	if scene == null:
		_finish()
		return

	var root := scene.instantiate()
	get_root().add_child(root)
	await process_frame
	await process_frame

	var simulation := root.get_node_or_null("FactorySimulation")
	var renderer := root.get_node_or_null("FactoryRender")
	_check(simulation != null, "simulation bridge exists")
	_check(renderer != null, "render bridge exists")
	if simulation != null:
		var statistics: Dictionary = simulation.get_statistics()
		_check(statistics.get("machine_count", -1) == 10000, "10K machines exist in native storage")
		_check(statistics.get("active_machine_count", -1) == 10000, "machines begin active")
		simulation.stop_simulation()
		var tick_before: int = simulation.get_statistics().get("tick_count", -1)
		simulation.step_simulation(3)
		_check(simulation.get_statistics().get("tick_count", -1) == tick_before + 3, "manual fixed ticks run")
		simulation.set_test_active_ratio(0.1)
		_check(simulation.get_statistics().get("active_machine_count", -1) == 1000, "active list can sleep machines")
	if renderer != null:
		var render_statistics: Dictionary = renderer.get_render_statistics()
		_check(render_statistics.get("total_instances", -1) == 10000, "10K render instances extracted")
		_check(render_statistics.get("batch_count", 0) > 1, "instances are split into chunk batches")

	_check(get_node_count() < 100, "machine count does not inflate SceneTree node count")
	root.queue_free()
	await process_frame
	_finish()


func _check(condition: bool, label: String) -> void:
	if condition:
		print("PASS: ", label)
	else:
		failures.append(label)
		push_error("FAIL: " + label)


func _finish() -> void:
	if failures.is_empty():
		print("Factory bridge smoke test passed")
		quit(0)
	else:
		print("Factory bridge smoke test failed: ", failures)
		quit(1)
