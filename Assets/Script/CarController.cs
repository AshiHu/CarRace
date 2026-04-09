using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PrometheusCarController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════
    //  RÉFÉRENCES
    // ═══════════════════════════════════════════════════════════════

    [Header("Roues - Colliders")]
    public WheelCollider WC_FrontLeft;
    public WheelCollider WC_FrontRight;
    public WheelCollider WC_RearLeft;
    public WheelCollider WC_RearRight;

    [Header("Roues - Meshes")]
    public Transform Mesh_FrontLeft;
    public Transform Mesh_FrontRight;
    public Transform Mesh_RearLeft;
    public Transform Mesh_RearRight;

    [Header("Effets de fumée")]
    public ParticleSystem LeftTireSmoke_PS;
    public ParticleSystem RightTireSmoke_PS;

    // ═══════════════════════════════════════════════════════════════
    //  MOTEUR
    // ═══════════════════════════════════════════════════════════════

    [Header("Moteur")]
    public float motorTorque = 2000f;
    public float brakeTorque = 5000f;
    public float maxSpeed = 200f;

    [Range(0f, 1f)]
    public float engineBraking = 0.25f;

    // ═══════════════════════════════════════════════════════════════
    //  DIRECTION
    // ═══════════════════════════════════════════════════════════════

    [Header("Direction")]
    public float maxSteerAngle = 35f;

    [Range(2f, 20f)]
    public float steerSpeed = 9f;

    [Range(0f, 1f)]
    public float highSpeedSteerReduction = 0.45f;
    public float steerReductionMaxSpeed = 160f;

    // ═══════════════════════════════════════════════════════════════
    //  GRIP — valeurs raisonnables pour WheelCollider Unity
    // ═══════════════════════════════════════════════════════════════

    [Header("Grip")]
    [Tooltip("Ne pas dépasser 2.5 — au-delà, Unity devient instable")]
    [Range(0.8f, 2.5f)]
    public float baseLateralStiffness = 1.8f;

    [Range(0.8f, 2.5f)]
    public float baseForwardStiffness = 1.6f;

    // ═══════════════════════════════════════════════════════════════
    //  DRIFT — déclenché uniquement par impulsion frein + volant
    // ═══════════════════════════════════════════════════════════════

    [Header("Drift")]
    [Tooltip("Vitesse minimale pour déclencher le drift (km/h)")]
    public float driftMinSpeed = 45f;

    [Tooltip("Braquage minimum requis [0-1]")]
    [Range(0.1f, 0.8f)]
    public float driftMinSteer = 0.25f;

    [Tooltip("Grip arrière pendant le drift — plus bas = plus de glisse")]
    [Range(0.1f, 1.0f)]
    public float driftRearStiffness = 0.35f;

    [Tooltip("Durée garantie après l'impulsion (s)")]
    public float driftMinDuration = 0.35f;

    [Tooltip("Durée max si on maintient le volant (s)")]
    public float driftMaxDuration = 2.2f;

    [Tooltip("Vitesse de récupération du grip après le drift")]
    [Range(1f, 12f)]
    public float gripRecoverySpeed = 5f;

    // ═══════════════════════════════════════════════════════════════
    //  PRIVÉ
    // ═══════════════════════════════════════════════════════════════

    private Rigidbody rb;
    private float currentSteerAngle;
    private float currentRearStiffness;
    private float driftTimer;
    private bool brakeWasPressedLastFrame;

    private float inputV;   // vertical
    private float inputH;   // horizontal

    private enum DriftState { Gripping, Drifting, Recovering }
    private DriftState driftState = DriftState.Gripping;

    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        currentRearStiffness = baseLateralStiffness;
        ApplyBaseFriction();
    }

    void FixedUpdate()
    {
        inputV = Input.GetAxis("Vertical");
        inputH = Input.GetAxis("Horizontal");

        HandleMotor();
        HandleSteering();
        UpdateDriftFSM();
        ApplyDriftFriction();
        UpdateWheelMeshes();
        HandleSmoke();
    }

    // ═══════════════════════════════════════════════════════════════
    //  MOTEUR
    // ═══════════════════════════════════════════════════════════════

    void HandleMotor()
    {
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        bool atMax = speedKmh >= maxSpeed && inputV > 0f;
        float torque = atMax ? 0f : motorTorque * Mathf.Max(0f, inputV);

        // AWD 50/50
        WC_FrontLeft.motorTorque = torque * 0.5f;
        WC_FrontRight.motorTorque = torque * 0.5f;
        WC_RearLeft.motorTorque = torque * 0.5f;
        WC_RearRight.motorTorque = torque * 0.5f;

        // Calcul du freinage
        float braking = 0f;
        bool goingFwd = Vector3.Dot(rb.linearVelocity, transform.forward) > 0.3f;
        bool goingBack = Vector3.Dot(rb.linearVelocity, transform.forward) < -0.3f;

        if (inputV < -0.05f && goingFwd) braking = Mathf.Abs(inputV);
        else if (inputV > 0.05f && goingBack) braking = inputV;
        else if (Mathf.Abs(inputV) < 0.05f) braking = engineBraking;

        float fb = brakeTorque * braking;
        // Répartition 65/35 avant/arrière — plus stable qu'un frein 50/50
        WC_FrontLeft.brakeTorque = fb * 0.65f;
        WC_FrontRight.brakeTorque = fb * 0.65f;
        WC_RearLeft.brakeTorque = fb * 0.35f;
        WC_RearRight.brakeTorque = fb * 0.35f;
    }

    // ═══════════════════════════════════════════════════════════════
    //  DIRECTION
    // ═══════════════════════════════════════════════════════════════

    void HandleSteering()
    {
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        float t = Mathf.Clamp01(speedKmh / steerReductionMaxSpeed);
        float limit = Mathf.Lerp(1f, highSpeedSteerReduction, t);
        float target = maxSteerAngle * limit * inputH;

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, target,
                                        steerSpeed * Time.fixedDeltaTime);

        WC_FrontLeft.steerAngle = currentSteerAngle;
        WC_FrontRight.steerAngle = currentSteerAngle;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MACHINE À ÉTATS DRIFT
    //  Gripping → [impulsion S + vitesse + volant] → Drifting
    //  Drifting → [minDuration passé + volant relâché | maxDuration] → Recovering
    //  Recovering → [stiffness revenu] → Gripping
    // ═══════════════════════════════════════════════════════════════

    void UpdateDriftFSM()
    {
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;

        // Impulsion = S pressé CE frame seulement (pas maintenu)
        bool brakeDown = inputV < -0.05f;
        bool brakeImpulse = brakeDown && !brakeWasPressedLastFrame;
        brakeWasPressedLastFrame = brakeDown;

        switch (driftState)
        {
            case DriftState.Gripping:
                if (brakeImpulse
                    && speedKmh >= driftMinSpeed
                    && Mathf.Abs(inputH) >= driftMinSteer)
                {
                    driftState = DriftState.Drifting;
                    driftTimer = 0f;
                }
                break;

            case DriftState.Drifting:
                driftTimer += Time.fixedDeltaTime;

                bool canExit = driftTimer >= driftMinDuration
                               && Mathf.Abs(inputH) < driftMinSteer;
                bool forced = driftTimer >= driftMaxDuration;

                if (canExit || forced)
                    driftState = DriftState.Recovering;
                break;

            case DriftState.Recovering:
                if (Mathf.Abs(currentRearStiffness - baseLateralStiffness) < 0.04f)
                {
                    currentRearStiffness = baseLateralStiffness;
                    driftState = DriftState.Gripping;
                }
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  FRICTION
    // ═══════════════════════════════════════════════════════════════

    void ApplyDriftFriction()
    {
        float targetRear = driftState == DriftState.Drifting
            ? driftRearStiffness
            : baseLateralStiffness;

        float speed = driftState == DriftState.Drifting
            ? 18f               // décrochage rapide
            : gripRecoverySpeed; // retour progressif

        currentRearStiffness = Mathf.Lerp(currentRearStiffness, targetRear,
                                           speed * Time.fixedDeltaTime);

        SetFriction(WC_FrontLeft, baseLateralStiffness, baseForwardStiffness);
        SetFriction(WC_FrontRight, baseLateralStiffness, baseForwardStiffness);
        SetFriction(WC_RearLeft, currentRearStiffness, baseForwardStiffness);
        SetFriction(WC_RearRight, currentRearStiffness, baseForwardStiffness);
    }

    void ApplyBaseFriction()
    {
        SetFriction(WC_FrontLeft, baseLateralStiffness, baseForwardStiffness);
        SetFriction(WC_FrontRight, baseLateralStiffness, baseForwardStiffness);
        SetFriction(WC_RearLeft, baseLateralStiffness, baseForwardStiffness);
        SetFriction(WC_RearRight, baseLateralStiffness, baseForwardStiffness);
    }

    void SetFriction(WheelCollider w, float side, float fwd)
    {
        var sf = w.sidewaysFriction; sf.stiffness = side; w.sidewaysFriction = sf;
        var ff = w.forwardFriction; ff.stiffness = fwd; w.forwardFriction = ff;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MESHES + FUMÉE
    // ═══════════════════════════════════════════════════════════════

    void UpdateWheelMeshes()
    {
        Sync(WC_FrontLeft, Mesh_FrontLeft);
        Sync(WC_FrontRight, Mesh_FrontRight);
        Sync(WC_RearLeft, Mesh_RearLeft);
        Sync(WC_RearRight, Mesh_RearRight);
    }

    void Sync(WheelCollider col, Transform mesh)
    {
        if (!mesh) return;
        col.GetWorldPose(out Vector3 p, out Quaternion r);
        mesh.SetPositionAndRotation(p, r);
    }

    void HandleSmoke()
    {
        WC_RearLeft.GetGroundHit(out WheelHit hl);
        WC_RearRight.GetGroundHit(out WheelHit hr);

        bool smoke = driftState == DriftState.Drifting
                  && rb.linearVelocity.magnitude > 2f
                  && (Mathf.Abs(hl.sidewaysSlip) > 0.25f
                   || Mathf.Abs(hr.sidewaysSlip) > 0.25f);

        Smoke(LeftTireSmoke_PS, smoke);
        Smoke(RightTireSmoke_PS, smoke);
    }

    void Smoke(ParticleSystem ps, bool on)
    {
        if (!ps) return;
        if (on && !ps.isPlaying) ps.Play();
        if (!on && ps.isPlaying) ps.Stop();
    }

    // ═══════════════════════════════════════════════════════════════
    //  GIZMO
    // ═══════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (!rb) return;
        Gizmos.color = driftState == DriftState.Drifting ? Color.cyan
                     : driftState == DriftState.Recovering ? Color.yellow
                     : Color.green;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.4f, Vector3.one * 0.25f);
    }
}