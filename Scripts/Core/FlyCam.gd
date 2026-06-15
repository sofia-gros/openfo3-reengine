extends Camera3D

@export var sensitivity: float = 0.2
@export var default_speed: float = 30.0
@export var fast_speed_multiplier: float = 4.0

func _ready():
	# Make sure we don't start at zero if not desired, 
	# but Megaton.cs will set our position.
	pass

func _input(event):
	# Blender style: Rotate while holding Right Mouse Button
	if event is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT):
		# Horizontal rotation (Global Y)
		rotate_y(deg_to_rad(-event.relative.x * sensitivity))
		
		# Vertical rotation (Local X)
		var change = -event.relative.y * sensitivity
		var new_rot_x = rotation.x + deg_to_rad(change)
		# Clamp to avoid gimbal lock/flipping
		if new_rot_x > -1.5 and new_rot_x < 1.5:
			rotate_object_local(Vector3.RIGHT, deg_to_rad(change))

func _process(delta):
	var direction = Vector3.ZERO
	var b = transform.basis
	
	if Input.is_key_pressed(KEY_W): direction -= b.z
	if Input.is_key_pressed(KEY_S): direction += b.z
	if Input.is_key_pressed(KEY_A): direction -= b.x
	if Input.is_key_pressed(KEY_D): direction += b.x
	
	if direction != Vector3.ZERO:
		var speed = default_speed
		if Input.is_key_pressed(KEY_SHIFT):
			speed *= fast_speed_multiplier
		position += direction.normalized() * speed * delta
