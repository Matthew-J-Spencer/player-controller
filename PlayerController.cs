using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Hey developer!
/// If you have any questions, come chat with me on my Discord: https://discord.gg/GqeHHnhHpz
/// If you enjoy the controller, make sure you give the video a thumbs up: https://youtu.be/rJECT58CQHs
/// Have fun!
///
/// Love,
/// Tarodev
/// </summary>
public class PlayerController : MonoBehaviour {
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private Animator _anim;
    private FrameInputs _inputs;

    private void Update() {
        GatherInputs();

        HandleGrounding();

        HandleWalking();

        HandleJumping();

        HandleWallSlide();

        HandleWallGrab();

        HandleDashing();
    }

    #region Inputs

    private bool _facingLeft;

    private void GatherInputs() {
        _inputs.RawX = (int) Input.GetAxisRaw("Horizontal");
        _inputs.RawY = (int) Input.GetAxisRaw("Vertical");
        _inputs.X = Input.GetAxis("Horizontal");
        _inputs.Y = Input.GetAxis("Vertical");

        _anim.SetInteger("RawY", _inputs.RawY);

        _facingLeft = _inputs.RawX != 1 && (_inputs.RawX == -1 || _facingLeft);
        if (!_grabbing) SetFacingDirection(_facingLeft); // Don't turn while grabbing the wall
    }

    private void SetFacingDirection(bool left) {
        _anim.transform.rotation = left ? Quaternion.Euler(0, -90, 0) : Quaternion.Euler(0, 90, 0);
    }

    #endregion

    #region Detection

    [Header("Detection")] [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _grounderOffset = -1, _grounderRadius = 0.2f;
    [SerializeField] private float _wallCheckOffset = 0.5f, _wallCheckRadius = 0.05f;
    private bool _isAgainstLeftWall, _isAgainstRightWall, _pushingLeftWall, _pushingRightWall;
    public bool IsGrounded;
    public static event Action OnTouchedGround;

    private readonly Collider[] _ground = new Collider[1];
    private readonly Collider[] _leftWall = new Collider[1];
    private readonly Collider[] _rightWall = new Collider[1];

    private void HandleGrounding() {
        // Grounder
        var grounded = Physics.OverlapSphereNonAlloc(transform.position + new Vector3(0, _grounderOffset), _grounderRadius, _ground, _groundMask) > 0;

        if (!IsGrounded && grounded) {
            IsGrounded = true;
            _hasDashed = false;
            _hasJumped = false;
            _currentMovementLerpSpeed = 100;
            PlayRandomClip(_landClips);
            _anim.SetBool("Grounded", true);
            OnTouchedGround?.Invoke();
            transform.SetParent(_ground[0].transform);
        }
        else if (IsGrounded && !grounded) {
            IsGrounded = false;
            _timeLeftGrounded = Time.time;
            _anim.SetBool("Grounded", false);
            transform.SetParent(null);
        }

        // Wall detection
        _isAgainstLeftWall = Physics.OverlapSphereNonAlloc(transform.position + new Vector3(-_wallCheckOffset, 0), _wallCheckRadius, _leftWall, _groundMask) > 0;
        _isAgainstRightWall = Physics.OverlapSphereNonAlloc(transform.position + new Vector3(_wallCheckOffset, 0), _wallCheckRadius, _rightWall, _groundMask) > 0;
        _pushingLeftWall = _isAgainstLeftWall && _inputs.X < 0;
        _pushingRightWall = _isAgainstRightWall && _inputs.X > 0;
    }

    private void DrawGrounderGizmos() {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + new Vector3(0, _grounderOffset), _grounderRadius);
    }

    private void OnDrawGizmos() {
        DrawGrounderGizmos();
        DrawWallSlideGizmos();
    }

    #endregion

    #region Walking

    [Header("Walking")] [SerializeField] private float _walkSpeed = 4;
    [SerializeField] private float _acceleration = 2;
    [SerializeField] private float _currentMovementLerpSpeed = 100;

    private void HandleWalking() {
        // Slowly release control after wall jump
        _currentMovementLerpSpeed = Mathf.MoveTowards(_currentMovementLerpSpeed, 100, _wallJumpMovementLerp * Time.deltaTime);

        if (_dashing) return;
        // This can be done using just X & Y input as they lerp to max values, but this gives greater control over velocity acceleration
        var acceleration = IsGrounded ? _acceleration : _acceleration * 0.5f;

        if (Input.GetKey(KeyCode.LeftArrow)) {
            if (_rb.velocity.x > 0) _inputs.X = 0; // Immediate stop and turn. Just feels better
            _inputs.X = Mathf.MoveTowards(_inputs.X, -1, acceleration * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.RightArrow)) {
            if (_rb.velocity.x < 0) _inputs.X = 0;
            _inputs.X = Mathf.MoveTowards(_inputs.X, 1, acceleration * Time.deltaTime);
        }
        else {
            _inputs.X = Mathf.MoveTowards(_inputs.X, 0, acceleration * 2 * Time.deltaTime);
        }

        var idealVel = new Vector3(_inputs.X * _walkSpeed, _rb.velocity.y);
        // _currentMovementLerpSpeed should be set to something crazy high to be effectively instant. But slowed down after a wall jump and slowly released
        _rb.velocity = Vector3.MoveTowards(_rb.velocity, idealVel, _currentMovementLerpSpeed * Time.deltaTime);

        _anim.SetBool("Walking", _inputs.RawX != 0 && IsGrounded);
    }

    #endregion

    #region Jumping

    [Header("Jumping")] [SerializeField] private float _jumpForce = 15;
    [SerializeField] private float _fallMultiplier = 7;
    [SerializeField] private float _jumpVelocityFalloff = 8;
    [SerializeField] private ParticleSystem _jumpParticles;
    [SerializeField] private Transform _jumpLaunchPoof;
    [SerializeField] private float _wallJumpLock = 0.25f;
    [SerializeField] private float _wallJumpMovementLerp = 5;
    [SerializeField] private float _coyoteTime = 0.2f;
    [SerializeField] private bool _enableDoubleJump = true;
    private float _timeLeftGrounded = -10;
    private float _timeLastWallJumped;
    private bool _hasJumped;
    private bool _hasDoubleJumped;

    private void HandleJumping() {
        if (_dashing) return;
        if (Input.GetKeyDown(KeyCode.C)) {
            if (_grabbing || !IsGrounded && (_isAgainstLeftWall || _isAgainstRightWall)) {
                _timeLastWallJumped = Time.time;
                _currentMovementLerpSpeed = _wallJumpMovementLerp;
                ExecuteJump(new Vector2(_isAgainstLeftWall ? _jumpForce : -_jumpForce, _jumpForce)); // Wall jump
            }
            else if (IsGrounded || Time.time < _timeLeftGrounded + _coyoteTime || _enableDoubleJump && !_hasDoubleJumped) {
                if (!_hasJumped || _hasJumped && !_hasDoubleJumped) ExecuteJump(new Vector2(_rb.velocity.x, _jumpForce), _hasJumped); // Ground jump
            }
        }

        void ExecuteJump(Vector3 dir, bool doubleJump = false) {
            _rb.velocity = dir;
            _jumpLaunchPoof.up = _rb.velocity;
            _jumpParticles.Play();
            _anim.SetTrigger(doubleJump ? "DoubleJump" : "Jump");
            _hasDoubleJumped = doubleJump;
            _hasJumped = true;
        }

        // Fall faster and allow small jumps. _jumpVelocityFalloff is the point at which we start adding extra gravity. Using 0 causes floating
        if (_rb.velocity.y < _jumpVelocityFalloff || _rb.velocity.y > 0 && !Input.GetKey(KeyCode.C))
            _rb.velocity += _fallMultiplier * Physics.gravity.y * Vector3.up * Time.deltaTime;
    }

    #endregion

    #region Wall Slide

    [Header("Wall Slide")] [SerializeField]
    private ParticleSystem _wallSlideParticles;

    [SerializeField] private float _slideSpeed = 1;
    private bool _wallSliding;

    private void HandleWallSlide() {
        var sliding = _pushingLeftWall || _pushingRightWall;

        if (sliding && !_wallSliding) {
            transform.SetParent(_pushingLeftWall ? _leftWall[0].transform : _rightWall[0].transform);
            _wallSliding = true;
            _wallSlideParticles.transform.position = transform.position + new Vector3(_pushingLeftWall ? -_wallCheckOffset : _wallCheckOffset, 0);
            _wallSlideParticles.Play();

            // Don't add sliding until actually falling or it'll prevent jumping against a wall
            if (_rb.velocity.y < 0) _rb.velocity = new Vector3(0, -_slideSpeed);
        }
        else if (!sliding && _wallSliding && !_grabbing) {
            transform.SetParent(null);
            _wallSliding = false;
            _wallSlideParticles.Stop();
        }
    }

    private void DrawWallSlideGizmos() {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + new Vector3(-_wallCheckOffset, 0), _wallCheckRadius);
        Gizmos.DrawWireSphere(transform.position + new Vector3(_wallCheckOffset, 0), _wallCheckRadius);
    }

    #endregion

    #region Wall Grab

    [Header("Wall Grab")] [SerializeField] private ParticleSystem _wallGrabParticles;
    private bool _grabbing;

    private void HandleWallGrab() {
        // I added wallJumpLock but I honestly can't remember why and I'm too scared to remove it...
        var grabbing = (_isAgainstLeftWall || _isAgainstRightWall) && Input.GetKey(KeyCode.Z) && Time.time > _timeLastWallJumped + _wallJumpLock;

        _rb.useGravity = !_grabbing;
        if (grabbing && !_grabbing) {
            _grabbing = true;
            _wallGrabParticles.transform.position = transform.position + new Vector3(_pushingLeftWall ? -_wallCheckOffset : _wallCheckOffset, 0);
            _wallGrabParticles.Play();
            SetFacingDirection(_isAgainstLeftWall);
        }
        else if (!grabbing && _grabbing) {
            _grabbing = false;
            _wallGrabParticles.Stop();
            Debug.Log("stopped");
        }

        if (_grabbing) _rb.velocity = new Vector3(0, _inputs.RawY * _slideSpeed * (_inputs.RawY < 0 ? 1 : 0.8f));

        _anim.SetBool("Climbing", _wallSliding || _grabbing);
    }

    #endregion

    #region Dash

    [Header("Dash")] [SerializeField] private float _dashSpeed = 15;
    [SerializeField] private float _dashLength = 1;
    [SerializeField] private ParticleSystem _dashParticles;
    [SerializeField] private Transform _dashRing;
    [SerializeField] private ParticleSystem _dashVisual;

    public static event Action OnStartDashing, OnStopDashing;

    private bool _hasDashed;
    private bool _dashing;
    private float _timeStartedDash;
    private Vector3 _dashDir;

    private void HandleDashing() {
        if (Input.GetKeyDown(KeyCode.X) && !_hasDashed) {
            _dashDir = new Vector3(_inputs.RawX, _inputs.RawY).normalized;
            if (_dashDir == Vector3.zero) _dashDir = _facingLeft ? Vector3.left : Vector3.right;
            _dashRing.up = _dashDir;
            _dashParticles.Play();
            _dashing = true;
            _hasDashed = true;
            _timeStartedDash = Time.time;
            _rb.useGravity = false;
            _dashVisual.Play();
            PlayRandomClip(_dashClips);
            OnStartDashing?.Invoke();
        }

        if (_dashing) {
            _rb.velocity = _dashDir * _dashSpeed;

            if (Time.time >= _timeStartedDash + _dashLength) {
                _dashParticles.Stop();
                _dashing = false;
                // Clamp the velocity so they don't keep shooting off
                _rb.velocity = new Vector3(_rb.velocity.x, _rb.velocity.y > 3 ? 3 : _rb.velocity.y);
                _rb.useGravity = true;
                if (IsGrounded) _hasDashed = false;
                _dashVisual.Stop();
                OnStopDashing?.Invoke();
            }
        }
    }

    #endregion

    #region Impacts

    [Header("Collisions")] [SerializeField]
    private ParticleSystem _impactParticles;

    [SerializeField] private GameObject _deathExplosion;
    [SerializeField] private float _minImpactForce = 2;

    private void OnCollisionEnter(Collision collision) {
        if (collision.relativeVelocity.magnitude > _minImpactForce && IsGrounded) _impactParticles.Play();
    }

    private void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Death")) {
            Instantiate(_deathExplosion, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }

        _hasDashed = false;
    }

    #endregion

    #region Audio

    [Header("Audio")] [SerializeField] private AudioSource _source;
    [SerializeField] private AudioClip[] _landClips;
    [SerializeField] private AudioClip[] _dashClips;

    private void PlayRandomClip(AudioClip[] clips) {
        _source.PlayOneShot(clips[Random.Range(0, clips.Length)], 0.2f);
    }

    #endregion

    private struct FrameInputs {
        public float X, Y;
        public int RawX, RawY;
    }
}