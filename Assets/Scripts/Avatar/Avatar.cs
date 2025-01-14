﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum State {
	IDLE,
	WALKFWD,
	WALKBACK,
	CROUCH,
	AIRIDLE,
	ATTACK,
	DASHATTACK,
	AERIAL,
	TUMBLE,
	JUMPSQUAT,
	LANDLAG,
	SPECIAL,
	DEFEND,
	GROUNDSTUN,
	COMBO,
	ROLL,
	TECH,
	DASH,
	RUN,
	RUNSTOP,
	RUNTURN,
	MISSTECH,
	GETUP,
}

public enum Weapon {
	MELEE,
	RANGED
}

public class Avatar : MonoBehaviour {

	const float AXIS_TILT_THRESHOLD = 0.5f;
	const float TORSO_ROTATION_OFFSET = 35.0f;
	const float SHOT_MOMENTUM_TRANSFER_RATIO = 0.5f;
	const float SHOTGUN_VELOCITY_DRIFT_LIMIT = 0.8f;
	const float GROUND_STUN_THRESHOLD = 50.0f;
	const float TECH_WINDOW = 0.25f;
	const int MAX_METER = 3000;
	const int METER_CHARGE_RATE = 5;
	const int METER_DAMAGE_RATIO = 5;
	const int ILLUSION_TRAIL_LENGTH = 10;

	// ecb = environmental collision box
	ECBManager ecb;
	HitboxManager hitbox;
	InputManager input;
    FXManager fx;
	Animator anim;
	Rigidbody2D rb;
    AudioSource audio;

	GameObject[,] tracers;
	GameObject[] illusionTrails;

	Vector2 specialVector;
	Vector2 tempVec;
	Vector2 savedVelocity;
	Vector2 knockbackToApply;
	Vector3 lookDirection;
	Quaternion startRotation;
	Quaternion lookRotation;

	int illusionIdx;

	float comboTimer;
	float shotTimer;
	float reloadTimer;
	float techTimer;
	float stunTimer;
	float freezeTimer;

	bool applyGroundMotion = false;
	bool isGrounded = true;
	bool isHitstun = false;
	bool isActionable = true;
	bool isMovable = true;
	bool isCrouch = false;
	bool hasDoubleJump = true;
	bool specialMovement = false;
	bool specialStartup = false;
	bool isFreezeFrame = false;
	bool wasTechPressed = false;
	bool isReloading = false;
	bool isCombo = false;
	bool isShotCooldown = false;
    bool isParrying = false;

	[SerializeField] bool control = true;
	[SerializeField] State currentState;
	[SerializeField] Weapon currentWeapon;
	[SerializeField] UIManager ui;
	[SerializeField] public int playerID;

	[Header("Stats")]
	[SerializeField] float weight;
	[SerializeField] int maxhealth;
	[SerializeField] int health;
	[SerializeField] int clipSize;
	[SerializeField] int ammo;
	[SerializeField] int meter;
	[SerializeField] float damage = 0;

	[Header("Geometry")]
	[SerializeField] Material baseMaterial;
	[SerializeField] GameObject hipRotationBone;
	[SerializeField] GameObject spineRotationBone;
	[SerializeField] GameObject spineTopRotationBone;
	[SerializeField] GameObject headRotationBone;

	[Header("MoveProperties")]
	[SerializeField] float dashSpeed;
	[SerializeField] float runSpeed;
	[SerializeField] float walkSpeed;
	[SerializeField] float crawlSpeed;
	[SerializeField] float airDriftSpeed;
	[SerializeField] float airDriftAccel;
	[SerializeField] float airFriction;
	[SerializeField] float groundTraction;

	[Header("JumpProperties")]
	[SerializeField] float fullHopSpeed;
	[SerializeField] float shortHopSpeed;
	[SerializeField] float doubleJumpSpeed;
	[SerializeField] float fallAccel;
	[SerializeField] float terminalFallSpeed;
	[SerializeField] float fastFallSpeed;
	[SerializeField] float hardLandThresholdSpeed;

	[Header("AttackProperties")]
	[SerializeField] GameObject gunModel;
	[SerializeField] GameObject bladeModelR;
	[SerializeField] GameObject bladeModelL;
	[SerializeField] GameObject muzzleFlash;
	[SerializeField] GameObject tracerPrefab;
	[SerializeField] int tracersPerShot;
	[SerializeField] float reloadTime;
	[SerializeField] float shotCooldown;
	[SerializeField] float weaponSpread;
	[SerializeField] float shotForce;

	[Header("SpecialProperties")]
	[SerializeField] Material illusionMaterial;
	[SerializeField] SkinnedMeshRenderer modelMesh;
	[SerializeField] GameObject illusionPrefab;
	[SerializeField] float specialRange;
	[SerializeField] float specialSpeed;
	[SerializeField] int meterCost;

	public int Health {
		get { return health; }
	}

	public int MaxHealth {
		get { return maxhealth; }
	}

	public float Weight {
		get { return weight; }
	}

	public float Damage {
		get { return damage; }
	}

	void Start() {
		ecb = GetComponentInChildren<ECBManager>();
		hitbox = GetComponentInChildren<HitboxManager>();
		input = GetComponent<InputManager>();
        fx = GetComponent<FXManager>();
		anim = GetComponent<Animator>();
		rb = GetComponent<Rigidbody2D>();
		audio = GetComponent<AudioSource>();
		currentState = State.IDLE;
		currentWeapon = Weapon.MELEE;
		// bullets preloaded and reused for better runtime performance
		tracers = new GameObject[clipSize, tracersPerShot];
		for (int i = 0; i < clipSize; i++)
		{
			for (int j = 0; j < tracersPerShot; j++)
			{
				tracers[i, j] = Instantiate(tracerPrefab);
				tracers[i, j].GetComponent<Tracer>().SetOwner(this.gameObject);
			}
		}
		illusionTrails = new GameObject[ILLUSION_TRAIL_LENGTH];
		for (int i = 0; i < ILLUSION_TRAIL_LENGTH; i++)
		{
			illusionTrails[i] = Instantiate(illusionPrefab);
			illusionTrails[i].SetActive(false);
		}
		ammo = clipSize;
		health = maxhealth;
		meter = MAX_METER;
		startRotation = hipRotationBone.transform.rotation;
	}

	void Update() {
		if (control)
		{
			input.UpdateAxes();
			SetAnimStickDirection(input.moveAxes.direction);
			SetAnimTiltLevel(input.moveAxes.tiltLevel);
			ComboLink();

			if (ecb.IgnorePlatforms)
			{
				ecb.SetIgnorePlatforms(false);
			}
			if (isMovable)
			{
				AimRotation();
				if (isGrounded)
				{
					MovementInput();
				}
				else
				{
					PlatformPassthrough();
				}
			}
			if (isActionable)
			{
				ActionInput();
			}
		}
		if (specialMovement)
		{
			if (illusionIdx < ILLUSION_TRAIL_LENGTH)
			{
				illusionTrails[illusionIdx].transform.position = transform.position;
				illusionTrails[illusionIdx].transform.rotation = transform.rotation;
				illusionTrails[illusionIdx].SetActive(true);
				illusionTrails[illusionIdx].GetComponent<DolphIllusion>().SetRotation(lookRotation);
				illusionIdx++;
			}
		}
		else if (currentState != State.SPECIAL)
		{
			ApplyPhysics();
			GroundCheck();
			if (!isHitstun)
			{
				Move();
			}
		}
		if (meter < MAX_METER)
		{
			meter += METER_CHARGE_RATE;
			if (meter > MAX_METER)
			{
				meter = MAX_METER;
			}
		}
		UpdateUI();
		RunTimers();
	}

	void LateUpdate() {
		if (control)
			LookAtCursor();
	}

	#region Input

	void AimRotation() {
		if (currentWeapon == Weapon.RANGED)
		{
			if (input.aimAxes.x == 0.0f && input.aimAxes.y == 0.0f)
			{
				lookDirection = transform.forward;
				lookRotation = Quaternion.LookRotation(lookDirection);
			}
			else
			{
				lookDirection = new Vector3(input.aimAxes.x, input.aimAxes.y, 0.0f);
				lookRotation = Quaternion.LookRotation(lookDirection);
				if (input.aimAxes.x > 0.0f)
				{
					transform.eulerAngles = new Vector3(0.0f, 90.0f, 0.0f);
					anim.SetFloat("LookDirection", transform.forward.x);
				}
				else if (input.aimAxes.x < 0.0f)
				{
					transform.eulerAngles = new Vector3(0.0f, -90.0f, 0.0f);
					anim.SetFloat("LookDirection", transform.forward.x);
				}
			}
		}
	}

	void TurnAround() {
		if (transform.forward.x < 0.0f)
		{
			transform.eulerAngles = new Vector3(0.0f, 90.0f, 0.0f);
		}
		else
		{
			transform.eulerAngles = new Vector3(0.0f, -90.0f, 0.0f);
		}
		anim.SetFloat("LookDirection", transform.forward.x);
	}

	void ActionInput() {
		if (input.GetButtonDown(Button.JUMP))
		{
			if (hasDoubleJump)
			{
				TriggerOneFrame("JumpTrigger");
			}
		}
		if (input.cStick.isTapInput)
		{
			if (currentWeapon == Weapon.MELEE)
			{
                TriggerOneFrame("AttackTrigger", input.cStick.direction);
                {
                    if (isGrounded && !DirectionSameAsInput(input.cStick))
                    {
                        if (input.cStick.direction == AxesInfo.Direction.LEFT || input.cStick.direction == AxesInfo.Direction.RIGHT)
                        TurnAround();
                    }
                }
            }
		}
		else if (input.GetButtonDown(Button.ATTACK))
		{
			if (currentWeapon == Weapon.RANGED)
			{
				TriggerOneFrame("ShotTrigger");
				FireWeapon();
			}
			else
			{
				TriggerOneFrame("AttackTrigger", input.moveAxes.direction);
			}
		}
		else if (input.GetButtonDown(Button.DEFEND))
		{
			if (isGrounded && currentWeapon == Weapon.MELEE)
			{
				TriggerOneFrame("DefendTrigger", input.moveAxes.direction);
			}
		}
		else if (input.GetButtonDown(Button.SPECIAL))
		{
			if (currentWeapon == Weapon.MELEE && meter >= meterCost)
			{
				TriggerOneFrame("SpecialTrigger");
			}
		}
		else if (input.GetButtonDown(Button.SWAP))
		{
			ChangeWeapon();
		}
		else if (input.GetButtonDown(Button.RELOAD))
		{
			Reload();
		}
	}

	void PlatformPassthrough() {
		if (currentState == State.AIRIDLE)
		{
			if (input.moveAxes.direction == AxesInfo.Direction.DOWN)
			{
				if (input.moveAxes.tiltLevel == 2)
				{
					ecb.SetIgnorePlatforms(true);
				}
			}
		}
	}

	void MovementInput() {

		switch (currentState)
		{
		case (State.DASH):
			if (currentWeapon != Weapon.MELEE)
			{
				break;
			}
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (!DirectionSameAsInput(input.moveAxes))
				{
					TriggerOneFrame("IdleTrigger");
					TurnAround();
				}
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			default:
				break;
			}
			break;
		case (State.RUN):
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (currentWeapon != Weapon.MELEE)
				{
					TriggerOneFrame("WalkTrigger");
				}
				else if (!DirectionSameAsInput(input.moveAxes))
				{
					TriggerOneFrame("RunTurnTrigger");
				}
				break;
			case (AxesInfo.Direction.DOWN):
				if (input.moveAxes.tiltLevel == 2)
				{
					TriggerOneFrame("CrouchTrigger");
				}
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			case (AxesInfo.Direction.NONE):
				if (input.moveAxes.directionLast == AxesInfo.Direction.NONE)
				{
					TriggerOneFrame("RunStopTrigger");
				}
				break;
			default:
				break;
			}
			break;
		case (State.WALKFWD):
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (!DirectionSameAsInput(input.moveAxes))
				{
					TriggerOneFrame("IdleTrigger");
					TurnAround();
				}
				else if ((input.moveAxes.isTapInput || input.moveAxes.isBufferedTapInput) && currentWeapon == Weapon.MELEE)
				{
					TriggerOneFrame("DashTrigger");
				}
				break;
			case (AxesInfo.Direction.DOWN):
				if (input.moveAxes.isTapInput && currentWeapon == Weapon.MELEE)
				{
					if (ecb.SetIgnorePlatforms(true))
					{
						TriggerOneFrame("AirIdleTrigger");
					}
				}
				else if (input.moveAxes.tiltLevel == 2)
				{
					TriggerOneFrame("CrouchTrigger");
				}
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			case (AxesInfo.Direction.NONE):
				TriggerOneFrame("IdleTrigger");
				break;
			default:
				break;
			}
			break;
		case (State.CROUCH):
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (DirectionSameAsInput(input.moveAxes))
				{
					if ((input.moveAxes.isTapInput || input.moveAxes.isBufferedTapInput) && currentWeapon == Weapon.MELEE)
					{
						TriggerOneFrame("DashTrigger");
					}
					else
					{
						TriggerOneFrame("WalkTrigger");
					}
				}
				else
				{
					TriggerOneFrame("IdleTrigger");
					TurnAround();
				}
				break;
			case (AxesInfo.Direction.NONE):
				TriggerOneFrame("IdleTrigger");
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			default:
				break;
			}
			break;
		case (State.IDLE):
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (DirectionSameAsInput(input.moveAxes))
				{
					if ((input.moveAxes.isTapInput || input.moveAxes.isBufferedTapInput) && currentWeapon == Weapon.MELEE)
					{
						TriggerOneFrame("DashTrigger");
					}
					else
					{
						TriggerOneFrame("WalkTrigger");
					}
				}
				else
				{
					TurnAround();
				}
				break;
			case (AxesInfo.Direction.DOWN):
				if (input.moveAxes.isTapInput && currentWeapon == Weapon.MELEE)
				{
					if (ecb.SetIgnorePlatforms(true))
					{
						TriggerOneFrame("AirIdleTrigger");
					}
				}
				else if (input.moveAxes.tiltLevel == 2)
				{
					TriggerOneFrame("CrouchTrigger");
				}
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			default:
				break;
			}
			break;
		case (State.RUNSTOP):
		case (State.RUNTURN):
			break;
		default:
			switch (input.moveAxes.direction)
			{
			case (AxesInfo.Direction.LEFT):
			case (AxesInfo.Direction.RIGHT):
				if (DirectionSameAsInput(input.moveAxes))
				{
					if ((input.moveAxes.isTapInput || input.moveAxes.isBufferedTapInput) && currentWeapon == Weapon.MELEE)
					{
						TriggerOneFrame("DashTrigger");
					}
					else
					{
						TriggerOneFrame("WalkTrigger");
					}
				}
				else
				{
					TriggerOneFrame("IdleTrigger");
					TurnAround();
				}
				break;
			case (AxesInfo.Direction.DOWN):
				if (input.moveAxes.tiltLevel == 2)
				{
					TriggerOneFrame("CrouchTrigger");
				}
				break;
			case (AxesInfo.Direction.UP):
				// jump if tap jump
				break;
			default:
				break;
			}
			break;
		}
	}

	void BufferTechInput() {
		if (!wasTechPressed && input.GetButtonDown(Button.DEFEND))
		{
			techTimer = TECH_WINDOW;
			wasTechPressed = true;
		}
	}

	void ComboLink() {
		if (currentState == State.COMBO)
		{
			if (input.GetButtonDown(Button.ATTACK))
			{
				anim.SetBool("LinkCombo", true);
			}
		}
	}

	bool DirectionSameAsInput(AxesInfo axes) {
		if (axes.x * transform.forward.x > 0.0f)
		{
			return true;
		}
		else
		{
			return false;
		}
	}

	#endregion

	#region Physics

	void ApplyPhysics() {
		if (!isFreezeFrame)
		{
			if (isGrounded)
			{
				AddFriction();
				FreeFall(); // todo: make ground clamp instead 
			}
			else
			{
				FreeFall();
			}
		}
	}

	void AddFriction() {
		if (rb.velocity.x != 0.0f)
		{
			if (Mathf.Abs(rb.velocity.x) - groundTraction * Time.deltaTime <= 0.0f)
			{
				tempVec = rb.velocity;
				tempVec.x = 0.0f;
				rb.velocity = tempVec;
			}
			else
			{
				rb.velocity -= groundTraction * Mathf.Sign(rb.velocity.x) * Vector2.right * Time.deltaTime;
			}
		}
	}

	void TransferLandingMomentum() {
		tempVec = rb.velocity;
		tempVec.y = 0.0f;
		rb.velocity = tempVec;
	}

	void GroundCheck() {
		if (ecb.GroundedRaycast())
		{
			// if falling
			if (!isGrounded && rb.velocity.y < 0.0f)
			{
				if (isHitstun)
				{
					if (wasTechPressed && techTimer > 0.0f)
					{
						isHitstun = false;
						TriggerOneFrame("TechTrigger");
					}
					else
					{
						if (true) //add rebound 
						{
							
						}
						isHitstun = false;
						TriggerOneFrame("MissedTechTrigger");
					}
					rb.velocity = Vector2.zero;
				}
				else
				{
					
					if (currentState == State.AERIAL)
					{
						TriggerOneFrame("HardLandTrigger");
					}
					else if (rb.velocity.y < -hardLandThresholdSpeed)
					{
						TriggerOneFrame("HardLandTrigger");
					}
					else
					{
						TriggerOneFrame("SoftLandTrigger");
					}
					TransferLandingMomentum();
				}
				// snap to ground y
				Vector2 temp = rb.position;
				temp.y = ecb.GetGroundPositionY();
				rb.position = temp;
			}
		}
		else
		{
			// if walked off platform
			if (isGrounded)
			{
				TriggerOneFrame("AirIdleTrigger");
			}
		}
	}

	void FreeFall() {
		if (rb.velocity.y > -terminalFallSpeed)
		{
			rb.velocity += Vector2.down * fallAccel * Time.deltaTime;
			if (rb.velocity.y < -terminalFallSpeed)
			{
				tempVec = rb.velocity;
				tempVec.y = -terminalFallSpeed;
				rb.velocity = tempVec;
			}
		}
		if (rb.velocity.x > airDriftSpeed)
		{
			rb.velocity += Vector2.left * airDriftAccel * Time.deltaTime;
		}
		else if (rb.velocity.x < -airDriftSpeed)
		{
			rb.velocity += Vector2.right * airDriftAccel * Time.deltaTime;
		}
	}

	public void StartFreezeFrame(float time) {
		savedVelocity = rb.velocity;
		freezeTimer = time;
		isFreezeFrame = true;
		anim.speed = 0.0f;
		rb.constraints = RigidbodyConstraints2D.FreezeAll;
	}

	void EndFreezeFrame() {
		isFreezeFrame = false;
		anim.speed = 1.0f;
		rb.constraints = RigidbodyConstraints2D.FreezeRotation; //reset constraints to normal
		if (currentState == State.TUMBLE)
		{
			rb.velocity = knockbackToApply / 8.0f;
			techTimer = 0.0f;
			wasTechPressed = false;
            fx.PlayTrail(true);
		}
		else
		{
			rb.velocity = savedVelocity;
		}
	}

	public void TakeHit(int damage, float freezeTime, float stunTime, Vector2 knockback) {
		if (isParrying)
        {
            return;
        }
        //health -= damage;
		this.damage += damage;
		stunTimer = stunTime;
		isHitstun = true;
		knockbackToApply = knockback;
		if (isGrounded && knockback.magnitude < GROUND_STUN_THRESHOLD)
		{
			ChangeState(State.GROUNDSTUN);
			//stunTimer *= 0.5f;
			TriggerOneFrame("GroundStunTrigger");
		}
		else
		{
			ChangeState(State.TUMBLE);
			TriggerOneFrame("TumbleTrigger");
			isGrounded = false;
			hipRotationBone.transform.rotation = Quaternion.LookRotation(new Vector3(knockback.x, knockback.y), Vector3.up);
		}
		transform.eulerAngles = new Vector3(0.0f, Mathf.Sign(knockback.x) * -90.0f, 0.0f);
		StartFreezeFrame(freezeTime);
        fx.ShakeCamera(freezeTime);
	}

	#endregion

	#region Movement

	void Move() {
		if (isGrounded && isMovable)
		{
			ApplyGroundMotion();
		}
		else if (!isGrounded && currentState != State.TUMBLE) // airborne
		{
			ApplyAirMotion();
		}
	}

	void ApplyGroundMotion() {
		
		switch (currentState)
		{
		case (State.DASH):
		case (State.DASHATTACK):
			rb.velocity = dashSpeed * transform.forward;
			break;
		case (State.RUN):
			rb.velocity = runSpeed * transform.forward;
			break;
		case (State.WALKFWD):
			rb.velocity = walkSpeed * transform.forward;
			break;
		case (State.WALKBACK):
			rb.velocity = -walkSpeed * transform.forward;
			break;
		default:
			break;
		}
	}

	void ApplyAirMotion() {
		// aerial drift
		tempVec = rb.velocity;
		if (tempVec.x < airDriftSpeed || tempVec.x > -airDriftSpeed)
		{
			tempVec.x += input.moveAxes.x * airDriftAccel * Time.deltaTime;
		}
		rb.velocity = tempVec;
		// fast fall
		if (currentWeapon == Weapon.MELEE && input.moveAxes.direction == AxesInfo.Direction.DOWN && input.moveAxes.isTapInput)
		{
			if (rb.velocity.y < 0.0f && rb.velocity.y > -fastFallSpeed)
			{
				tempVec = rb.velocity;
				tempVec.y = -fastFallSpeed;
				rb.velocity = tempVec;
			}
		}
	}

	// called by animation event at end of JumpSquat
	void AddJumpForce() {
		// performs full hop if player is still holding jump at end of jump squat animation
		if (input.GetButtonHeld(Button.JUMP))
		{
			rb.velocity += Vector2.up * fullHopSpeed;
		}
		else
		{
			rb.velocity += Vector2.up * shortHopSpeed;
		}
		isGrounded = false;
	}

	// called by animation event
	void AddDoubleJumpForce() {
		tempVec = rb.velocity;
		tempVec.y = doubleJumpSpeed;
		// add aerial drift if player is holding direction
		tempVec.x = input.moveAxes.x * airDriftSpeed;
		rb.velocity = tempVec;
		hasDoubleJump = false;
	}

	#endregion

	#region AnimTools

	public void ActivateHitbox(Move move) {
		hitbox.ActivateHitbox(move);
	}

	public void DeactivateHitbox(Move move) {
		hitbox.DeactivateHitbox(move);
	}

	void TriggerOneFrame(string trigger, AxesInfo.Direction direction = AxesInfo.Direction.NONE) {
		SetAnimStickDirection(direction);
		StartCoroutine(TriggerOneFrameCoroutine(trigger));
	}

	void SetAnimStickDirection(AxesInfo.Direction direction) {
		if (transform.forward.x < 0.0f)
		{
			if (direction == AxesInfo.Direction.RIGHT)
			{
				direction = AxesInfo.Direction.LEFT;
			}
			else if (direction == AxesInfo.Direction.LEFT)
			{
				direction = AxesInfo.Direction.RIGHT;
			}
		}
		anim.SetInteger("StickDirection", (int)direction);
	}

	void SetAnimTiltLevel(int tilt) {
		anim.SetInteger("TiltLevel", tilt);
	}

	IEnumerator TriggerOneFrameCoroutine(string trigger) {
		anim.SetTrigger(trigger);
		yield return null;
		if (anim != null)
		{
			anim.ResetTrigger(trigger);
		}
	}

	void SetActionable(int i) {
		if (i == 0)
		{
			isActionable = false;
		}
		else if (i == 1)
		{
			isActionable = true;
		}
		else
		{
			print("Animator passed invalid argument to PlayableCharacter::SetActionable (must be 0 or 1)");
		}
	}

	void SetMovable(int i) {
		if (i == 0)
		{
			isMovable = false;
		}
		else if (i == 1)
		{
			isMovable = true;
		}
		else
		{
			print("Animator passed invalid argument to PlayableCharacter::SetMovable (must be 0 or 1)");
		}
	}

	void ChangeState(State newState) {

        if (currentState == State.TUMBLE && newState != State.TUMBLE)
        {
            fx.PlayTrail(false);
        }
        if (newState == State.DASH)
        {
            fx.PlayDashPoof();
        }
        if (newState == State.MISSTECH)
        {
            fx.PlayHardLandPoof();
        }
        if (currentState == State.ATTACK || currentState == State.DASHATTACK || currentState == State.AERIAL || currentState == State.COMBO || currentState == State.SPECIAL)
		{
			hitbox.ResetHitboxes();
		}
		if (newState == State.SPECIAL)
		{
			specialStartup = true;
		}
		else if (newState == State.AIRIDLE || newState == State.AERIAL)
		{
			isGrounded = false;
		}
		else
		{
			// if changing from airborne to grounded state
			if (!isGrounded)
			{
				hasDoubleJump = true;
			}
			isGrounded = true;
		}
		if (newState == State.ATTACK || newState == State.DASHATTACK || newState == State.SPECIAL || newState == State.AERIAL || newState == State.LANDLAG || newState == State.TUMBLE || newState == State.GROUNDSTUN || newState == State.COMBO)
		{
			isActionable = false;
			isMovable = false;
		}
		else
		{
			isActionable = true;
			isMovable = true;
		}
		if (newState == State.ATTACK || newState == State.DASHATTACK || newState == State.COMBO)
		{
			ecb.ToggleEdgeECB(true);
		}
		else
		{
			ecb.ToggleEdgeECB(false);
		}
		currentState = newState;
		anim.SetBool("LinkCombo", false);
	}

	void ChangeWeaponAnimCallback(int i) {
		isActionable = true;
		if (i == 0)
		{
			currentWeapon = Weapon.RANGED;
			gunModel.SetActive(true);
			bladeModelR.SetActive(false);
			bladeModelL.SetActive(false);
		}
		else if (i == 1)
		{
			currentWeapon = Weapon.MELEE;
			gunModel.SetActive(false);
			bladeModelR.SetActive(true);
			bladeModelL.SetActive(true);
		}
		else
		{
			print("Animator passed invalid argument to PlayableCharacter::ChangeWeaponAnimCallback (must be 0 or 1)");
		}
	}

	void ChangeWeapon() {
		isActionable = false;
		if (currentWeapon == Weapon.MELEE)
		{
			anim.SetBool("isMelee", false);
		}
		else
		{
			anim.SetBool("isMelee", true);
		}
	}

	void StartSpecial() {
		if (input.spcAxes.x == 0.0f && input.spcAxes.y == 0.0f)
		{
			specialVector = new Vector2(transform.forward.x, transform.forward.y);
		}
		else
		{
			if (Mathf.Sign(input.spcAxes.x) != Mathf.Sign(transform.forward.x))
			{
				TurnAround();
			}
			specialVector = new Vector2(input.spcAxes.x, input.spcAxes.y);
		}
		if (isGrounded && specialVector.y < 0.0f)
		{
			specialVector.y = 0.0f;
		}
		specialVector.Normalize();
		specialMovement = true;
		specialStartup = false;
		modelMesh.material = illusionMaterial;
		rb.velocity = specialVector * specialSpeed;
		meter -= meterCost;
	}

	void ExitSpecial() {
		illusionIdx = 0;
		specialMovement = false;
		modelMesh.material = baseMaterial;

		if (ecb.GroundedRaycast())
		{
			isGrounded = true;
            hasDoubleJump = true;
			if (specialVector.y < -0.2f)
			{
				anim.SetTrigger("HardLandTrigger");
			}
			else
			{
				anim.SetTrigger("SoftLandTrigger");
			}
			//TransferLandingMomentum();
			rb.velocity = Vector2.zero;
		}
		else
		{
			rb.velocity = specialVector * (airDriftSpeed / 2.0f);
			isGrounded = false;
			anim.SetTrigger("AirIdleTrigger");
		}
	}

    void ActivateParry(int active)
    {
        if (active == 0)
        {
            isParrying = false;
        }
        else
        {
            isParrying = true;
        }
    }

    void ChangeMaterial(int i) {
		if (i == 0)
		{
			modelMesh.material = baseMaterial;
		}
		else if (i == 1)
		{
			modelMesh.material = illusionMaterial;
		}
	}

	void Reload() {
		if (!isReloading)
		{
			reloadTimer = reloadTime;
			isReloading = true;
		}
	}

	void FireWeapon() {
		if (ammo > 0)
		{
			if (!isShotCooldown)
				ShotgunBlast();
		}
	}

	void ShotgunBlast() {
		float spread;
		float drift;
		ammo--;
		isShotCooldown = true;
		shotTimer = shotCooldown;
		muzzleFlash.SetActive(true);
		audio.Play();
		for (int i = 0; i < tracersPerShot; i++)
		{
			spread = Random.Range(-weaponSpread, weaponSpread);
			drift = Random.Range(SHOTGUN_VELOCITY_DRIFT_LIMIT, 1.0f);
			tracers[ammo, i].transform.position = muzzleFlash.transform.position;
			tracers[ammo, i].transform.rotation = lookRotation;
			tracers[ammo, i].transform.Rotate(spread, 0.0f, 0.0f);
			tracers[ammo, i].SetActive(true);
			tracers[ammo, i].GetComponent<Rigidbody>().velocity = (tracers[ammo, i].transform.forward * shotForce * drift) + (new Vector3(rb.velocity.x, rb.velocity.y) * SHOT_MOMENTUM_TRANSFER_RATIO);
		}
		if (ammo <= 0)
		{
			Reload();
		}

	}

	void LookAtCursor() {
		if (currentWeapon == Weapon.RANGED)
		{
			lookRotation = Quaternion.LookRotation(lookDirection);
			spineRotationBone.transform.rotation = lookRotation;
			Vector3 temp = spineTopRotationBone.transform.localEulerAngles;
			temp.y += TORSO_ROTATION_OFFSET;
			spineTopRotationBone.transform.localEulerAngles = temp;
			headRotationBone.transform.rotation = lookRotation;
		}
		else if (currentState == State.SPECIAL)
		{
			lookRotation = Quaternion.LookRotation(new Vector3(specialVector.x, specialVector.y));
			spineRotationBone.transform.rotation = lookRotation;
		}
	}

	#endregion

	#region Misc

    public bool IsTumble()
    {
        if (currentState == State.TUMBLE)
        {
            return true;
        }
        return false;
    }

    public bool IsFreezeFrame()
    {
        return isFreezeFrame;
    }

	public void AddMeter(int damage) {
		if (meter < MAX_METER)
		{
			meter += damage * METER_DAMAGE_RATIO;
			if (meter > MAX_METER)
			{
				meter = MAX_METER;
			}
		}
	}

	void RunTimers() {
		if (isFreezeFrame)
		{
			freezeTimer -= Time.deltaTime;
			if (freezeTimer <= 0.0f)
			{
				EndFreezeFrame();
			}
		}
		else if (isHitstun)
		{
			stunTimer -= Time.deltaTime;
			if (stunTimer <= 0.0f)
			{
				isHitstun = false;
				if (currentState == State.TUMBLE)
				{
					TriggerOneFrame("AirIdleTrigger");
				}
				else
				{
					TriggerOneFrame("GroundStunEndTrigger");
				}
			}
		}
		if (isShotCooldown)
		{
			shotTimer -= Time.deltaTime;
			if (shotTimer <= 0.0f)
			{
				isShotCooldown = false;
			}
		}
		if (isReloading)
		{
			reloadTimer -= Time.deltaTime;
			if (reloadTimer <= 0.0f)
			{
				ammo = clipSize;
				isReloading = false;
			}
		}

		if (wasTechPressed)
		{
			techTimer -= Time.deltaTime;
		}
	}

	void UpdateUI() {
		ui.SetAmmo(ammo, playerID);
		ui.SetMeter(meter, MAX_METER, playerID);
		ui.SetDamage((int)damage, playerID);
	}

	public void Respawn(Vector3 position) {
		ammo = clipSize;
		damage = 0.0f;
		meter = MAX_METER;
		rb.transform.position = position;
		rb.velocity = Vector2.zero;
		isGrounded = true;
		hasDoubleJump = true;
        specialMovement = false;
        modelMesh.material = baseMaterial;
        fx.PlayTrail(false);
        currentState = State.IDLE;
        anim.SetTrigger("RespawnTrigger");
	}

	#endregion
}
