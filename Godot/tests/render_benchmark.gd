extends SceneTree

const COUNTS: Array[int] = [10000, 50000, 100000]
const SETTLE_FRAMES := 45
const SAMPLE_FRAMES := 120


func _init() -> void:
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var packed_scene := load("res://scenes/render_benchmark.tscn") as PackedScene
	if packed_scene == null:
		push_error("Unable to load render benchmark scene")
		quit(1)
		return

	var scene := packed_scene.instantiate()
	get_root().add_child(scene)
	await process_frame
	var simulation = scene.get_node("FactorySimulation")
	var renderer = scene.get_node("FactoryRender")
	if not simulation.has_method("create_test_machines") or not renderer.has_method("rebuild_render_batches"):
		push_error("Factory native extension is not loaded")
		scene.queue_free()
		await process_frame
		quit(3)
		return
	print("instances,visible_instances,batches,draw_calls,objects,wall_frame_mean_ms,wall_frame_p95_ms,cpu_frame_setup_ms,gpu_render_ms,rebuild_ms,visibility_ms,upload_bytes")

	for count in COUNTS:
		printerr("BENCHMARK_BEGIN:", count)
		if not simulation.create_test_machines(count):
			push_error("Failed to create %d machines" % count)
			quit(2)
			return
		renderer.rebuild_render_batches()
		printerr("BENCHMARK_BATCHES_READY:", count)
		for frame in SETTLE_FRAMES:
			await process_frame

		var cpu_total := 0.0
		var gpu_total := 0.0
		var draw_calls_total := 0.0
		var objects_total := 0.0
		var wall_frame_samples: Array[float] = []
		wall_frame_samples.resize(SAMPLE_FRAMES)
		var previous_frame_usec := Time.get_ticks_usec()
		for frame in SAMPLE_FRAMES:
			await process_frame
			var current_frame_usec := Time.get_ticks_usec()
			wall_frame_samples[frame] = (current_frame_usec - previous_frame_usec) / 1000.0
			previous_frame_usec = current_frame_usec
			cpu_total += RenderingServer.get_frame_setup_time_cpu()
			gpu_total += RenderingServer.viewport_get_measured_render_time_gpu(get_root().get_viewport_rid())
			draw_calls_total += Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
			objects_total += Performance.get_monitor(Performance.RENDER_TOTAL_OBJECTS_IN_FRAME)

		wall_frame_samples.sort()
		var wall_frame_total := 0.0
		for sample in wall_frame_samples:
			wall_frame_total += sample
		var wall_frame_p95 := wall_frame_samples[int((SAMPLE_FRAMES - 1) * 0.95)]
		var statistics: Dictionary = renderer.get_render_statistics()
		print("%d,%d,%d,%.2f,%.2f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%d" % [
			count,
			statistics.get("visible_instances", 0),
			statistics.get("batch_count", 0),
			draw_calls_total / SAMPLE_FRAMES,
			objects_total / SAMPLE_FRAMES,
			wall_frame_total / SAMPLE_FRAMES,
			wall_frame_p95,
			cpu_total / SAMPLE_FRAMES,
			gpu_total / SAMPLE_FRAMES,
			statistics.get("last_rebuild_ms", 0.0),
			statistics.get("last_visibility_ms", 0.0),
			statistics.get("uploaded_bytes", 0),
		])

	scene.queue_free()
	await process_frame
	quit(0)
