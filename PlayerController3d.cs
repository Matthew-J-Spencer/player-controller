using System;
using System.Linq;
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
public class PlayerController3d : MonoBehaviour {
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

    private void GatherInputs() {
        _inputs.RawX = (int) Input.GetAxisRaw("Horizontal");
        _inputs.RawZ = (int) Input.GetAxisRaw("Vertical");
        _inputs.X = Input.GetAxis("Horizontal");
        _inputs.Z = Input.GetAxis("Vertical");

        _dir = new Vector3(_inputs.X, 0, _inputs.Z);

        // Set look direction only if dir is not zero, to avoid snapping back to original
        if (_dir != Vector3.zero && !_grabbing && !_wallSliding) _anim.transform.forward = _dir;

        _anim.SetInteger("RawZ", _inputs.RawZ);
    }

    #endregion

    #region Detection

    [Header("Detection")] [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _grounderOffset = -1, _grounderRadius = 0.2f;
    [SerializeField] private float _wallCheckOffset = 0.5f, _wallCheckRadius = 0.38f;
    private bool _isAgainstWall, _pushingWall;
    public bool IsGrounded;
    public static event Action OnTouchedGround;

    private readonly Collider[] _ground = new Collider[1];
    private readonly Collider[] _wall = new Collider[1];

    private void HandleGrounding() {
        // Grounder
        var grounded = Physics.OverlapSphereNonAlloc(transform.position + new Vector3(0, _grounderOffset), _grounderRadius, _ground, _groundMask) > 0;

        if (!IsGrounded && grounded) {
            IsGrounded = true;
            _hasJumped = false;
            _hasDashed = false;
            _currentMovementLerpSpeed = 100;
            PlayRandomClip(_landClips);
            _anim.SetBool("Grounded", true);
            OnTouchedGround?.Invoke();
        }
        else if (IsGrounded && !grounded) {
            IsGrounded = false;
            _anim.SetBool("Grounded", false);
            transform.SetParent(null);
        }

        // Wall detection
        _isAgainstWall = Physics.OverlapSphereNonAlloc(WallDetectPosition, _wallCheckRadius, _wall, _groundMask) > 0;
        _pushingWall = _isAgainstWall && _inputs.X < 0;
    }

    private Vector3 WallDetectPosition => _anim.transform.position + Vector3.up + _anim.transform.forward * _wallCheckOffset;


    private void OnDrawGizmos() {
        Gizmos.color = Color.red;

        // Grounder
        Gizmos.DrawWireSphere(transform.position + new Vector3(0, _grounderOffset), _grounderRadius);

        // Wall
        Gizmos.DrawWireSphere(WallDetectPosition, _wallCheckRadius);
    }

    #endregion

    #region Walking

    [Header("Walking")] [SerializeField] private float _walkSpeed = 8;
    [SerializeField] private float _acceleration = 2;
    [SerializeField] private float _maxWalkingPenalty = 0.5f;
    [SerializeField] private float _currentMovementLerpSpeed = 100;
    private float _currentWalkingPenalty;

    private Vector3 _dir;

    /// <summary>
    /// I'm sure this section could use a big refactor
    /// </summary>
    private void HandleWalking() {
        _currentMovementLerpSpeed = Mathf.MoveTowards(_currentMovementLerpSpeed, 100, _wallJumpMovementLerp * Time.deltaTime);

        var normalizedDir = _dir.normalized;

        // Slowly increase max speed
        if (_dir != Vector3.zero) _currentWalkingPenalty += _acceleration * Time.deltaTime;
        else _currentWalkingPenalty -= _acceleration * Time.deltaTime;
        _currentWalkingPenalty = Mathf.Clamp(_currentWalkingPenalty, _maxWalkingPenalty, 1);

        // Set current y vel and add walking penalty
        var targetVel = new Vector3(normalizedDir.x, _rb.velocity.y, normalizedDir.z) * _currentWalkingPenalty * _walkSpeed;

        // Set vel
        var idealVel = new Vector3(targetVel.x, _rb.velocity.y, targetVel.z);

        _rb.velocity = Vector3.MoveTowards(_rb.velocity, idealVel, _currentMovementLerpSpeed * Time.deltaTime);

        _anim.SetBool("Walking", _dir != Vector3.zero && IsGrounded);
    }

    #endregion

    #region Jumping

    [Header("Jumping")] [SerializeField] private float _jumpForce = 15;
    [SerializeField] private float _fallMultiplier = 7;
    [SerializeField] private float _jumpVelocityFalloff = 8;
    [SerializeField] private ParticleSystem _jumpParticles;
    [SerializeField] private Transform _jumpLaunchPoof;
    [SerializeField] private float _wallJumpLock = 0.25f;
    [SerializeField] private float _wallJumpMovementLerp = 20;
    [SerializeField] private float _coyoteTime = 0.3f;
    [SerializeField] private bool _enableDoubleJump = true;
    private float _timeLeftGrounded = -10;
    private float _timeLastWallJumped;
    private bool _hasJumped;
    private bool _hasDoubleJumped;

    private void HandleJumping() {
        if (Input.GetButtonDown("Fire2")) {
            if (!IsGrounded && _isAgainstWall) {
                _timeLastWallJumped = Time.time;
                _currentMovementLerpSpeed = _wallJumpMovementLerp;

                if (GetWallHit(out var wallHit)) ExecuteJump(new Vector3(wallHit.normal.x * _jumpForce, _jumpForce, wallHit.normal.z * _jumpForce)); // Wall jump
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
        if (_rb.velocity.y < _jumpVelocityFalloff || _rb.velocity.y > 0 && !Input.GetButton("Fire2"))
            _rb.velocity += _fallMultiplier * Physics.gravity.y * Vector3.up * Time.deltaTime;
    }

    #endregion

    #region Wall Slide

    [Header("Wall Slide")] [SerializeField]
    private ParticleSystem _wallSlideParticles;

    [SerializeField] private float _slideSpeed = 2;
    private bool _wallSliding;

    private void HandleWallSlide() {
        if (_pushingWall && !_wallSliding) {
            _wallSliding = true;
            _wallSlideParticles.Play();

            if (GetWallHit(out var wallHit)) {
                // Face wall
                _anim.transform.forward = -wallHit.normal;

                // Move closer to wall
                var hitPos = new Vector3(wallHit.point.x, transform.position.y, wallHit.point.z);
                if (Vector3.Distance(transform.position, wallHit.point) > 0.5f) transform.position = Vector3.MoveTowards(transform.position, hitPos, 0.4f);
            }
        }
        else if (!_pushingWall && _wallSliding && !_grabbing) {
            _wallSliding = false;
            _wallSlideParticles.Stop();
        }

        if (_wallSliding) // Don't add sliding until actually falling or it'll prevent jumping against a wall
            if (_rb.velocity.y < 0)
                _rb.velocity = new Vector3(0, -_slideSpeed);
    }

    private bool GetWallHit(out RaycastHit outHit) {
        if (Physics.Raycast(_anim.transform.position + Vector3.up, _anim.transform.forward, out var hit, 2, _groundMask)) {
            outHit = hit;
            return true;
        }

        outHit = new RaycastHit();
        return false;
    }

    #endregion

    #region Wall Grab

    [Header("Wall Grab")] [SerializeField] private ParticleSystem _wallGrabParticles;
    private bool _grabbing;

    private void HandleWallGrab() {
        // I added wallJumpLock but I honestly can't remember why and I'm too scared to remove it...
        var grabbing = _isAgainstWall && Input.GetButton("Fire1") && Time.time > _timeLastWallJumped + _wallJumpLock;

        _rb.useGravity = !_grabbing;
        if (grabbing && !_grabbing) {
            _grabbing = true;
            _wallGrabParticles.Play();
        }
        else if (!grabbing && _grabbing) {
            _grabbing = false;
            _wallGrabParticles.Stop();
        }

        if (_grabbing) _rb.velocity = new Vector3(0, _inputs.RawZ * _slideSpeed * (_inputs.RawZ < 0 ? 1 : 0.8f));

        _anim.SetBool("Climbing", _wallSliding || _grabbing);
    }

    #endregion

    #region Dash

    [Header("Dash")] [SerializeField] private float _dashSpeed = 30;
    [SerializeField] private float _dashLength = 0.2f;
    [SerializeField] private ParticleSystem _dashParticles;
    [SerializeField] private Transform _dashRing;
    [SerializeField] private ParticleSystem _dashVisual;
    [SerializeField] private float _dashTargetCastRadius = 4;
    [SerializeField] private float _dashTargetCastExtent = 6;
    [SerializeField] private float _dashTargetCastDistance = 15;
    [SerializeField] private LayerMask _dashTargetMask;
    [SerializeField] private bool _useDashTargets = true;

    public static event Action OnStartDashing, OnStopDashing;

    private bool _hasDashed;
    private bool _dashing;
    private float _timeStartedDash;
    private Vector3 _dashDir;
    private bool _dashingToTarget;

    private void HandleDashing() {
        if (Input.GetButtonDown("Fire3") && !_hasDashed) {
            _dashDir = new Vector3(_inputs.RawX, 0, _inputs.RawZ).normalized;
            if (_dashDir == Vector3.zero) _dashDir = _anim.transform.forward;

            if (_useDashTargets) {
                var targets = Physics.CapsuleCastAll(transform.position + new Vector3(0, _dashTargetCastExtent) + _anim.transform.forward,
                    transform.position - new Vector3(0, _dashTargetCastExtent) + _anim.transform.forward, _dashTargetCastRadius, _anim.transform.forward, _dashTargetCastDistance, _dashTargetMask);

                var closestTarget = targets.Select(t => t.transform).OrderBy(t => Vector3.Distance(transform.position, t.position)).FirstOrDefault();

                if (closestTarget != null) {
                    _dashDir = (closestTarget.position - transform.position).normalized;
                    _dashingToTarget = true;
                }
            }

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

            if (Time.time >= _timeStartedDash + _dashLength && !_dashingToTarget) {
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
        if (collision.transform.CompareTag("Death")) ExecuteDeath();
        if (collision.relativeVelocity.magnitude > _minImpactForce && IsGrounded) _impactParticles.Play();
    }

    private void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Death")) ExecuteDeath();
        else if (other.CompareTag("DashTarget"))
            _dashingToTarget = false;
        else // This will be a dash point. Should probably specify this
            _hasDashed = false;
    }

    private void ExecuteDeath() {
        Instantiate(_deathExplosion, transform.position, Quaternion.identity);
        Destroy(gameObject);
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
        public float X, Z;
        public int RawX, RawZ;
    }
}