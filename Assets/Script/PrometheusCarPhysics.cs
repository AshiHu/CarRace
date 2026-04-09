using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PrometheusCarPhysics : MonoBehaviour
{
 

    [Header("Roues - Colliders")]
    public WheelCollider WC_FrontLeft;
    public WheelCollider WC_FrontRight;
    public WheelCollider WC_RearLeft;
    public WheelCollider WC_RearRight;

  

    [Header("Rigidbody")]
    [Tooltip("Masse de la voiture (kg). TM Stadium Car ≈ 900 kg")]
    public float carMass = 900f;

    [Tooltip("Centre de masse. Y négatif = voiture plus stable.\n" +
             "Commence à -0.5, descends si tu te retournes encore.")]
    public Vector3 centerOfMass = new Vector3(0f, -0.5f, 0.1f);

    [Tooltip("Amortissement angulaire — réduit les oscillations de roulis/tangage")]
    public float angularDrag = 0.15f;

   

    [Header("Suspensions (appliquées automatiquement)")]
    [Tooltip("Rigidité du ressort. TM : raide = 35000-45000")]
    public float suspensionSpring = 38000f;

    [Tooltip("Amortisseur. Doit être environ 10-15% du spring")]
    public float suspensionDamper = 4200f;

    [Tooltip("Position cible de la suspension [0-1]. 0.3 = légèrement comprimée")]
    [Range(0f, 1f)]
    public float suspensionTarget = 0.3f;

    [Tooltip("Débattement total de la suspension (m)")]
    public float suspensionDistance = 0.15f;

    

    [Header("Anti-roll")]
    [Tooltip("Force anti-roulis sur l'essieu avant.\n" +
             "Trop élevé = sous-virage. Trop bas = retournement.\n" +
             "Commence à 5000, ajuste par paliers de 1000.")]
    public float antiRollFront = 5000f;

    [Tooltip("Force anti-roulis sur l'essieu arrière.\n" +
             "Doit être légèrement inférieur à l'avant pour garder le drift.")]
    public float antiRollRear = 4000f;

  

    [Header("Downforce")]
    [Tooltip("Force d'appui aérodynamique à vitesse maximale (N).\n" +
             "Stabilise sans affecter le drift à basse vitesse.")]
    public float downforceMax = 800f;

    [Tooltip("Vitesse à partir de laquelle la downforce est maximale (km/h)")]
    public float downforceMaxSpeed = 150f;



    [Header("Stabilisation aérienne")]
    public float airStabTorque = 15f;
    public float airRotForce = 4f;


    private Rigidbody rb;
    private bool isGrounded;



    void Awake()
    {
        rb = GetComponent<Rigidbody>();

    
        rb.mass = carMass;
        rb.angularDamping = angularDrag;
        rb.centerOfMass = centerOfMass;

     
        ConfigureSuspensions();
    }

    void ConfigureSuspensions()
    {
        foreach (var wc in new[] { WC_FrontLeft, WC_FrontRight, WC_RearLeft, WC_RearRight })
        {
            if (wc == null) continue;

            wc.suspensionDistance = suspensionDistance;

            var spring = wc.suspensionSpring;
            spring.spring = suspensionSpring;
            spring.damper = suspensionDamper;
            spring.targetPosition = suspensionTarget;
            wc.suspensionSpring = spring;

  
            SetFrictionCurve(wc, true, 0.4f, 1.0f, 0.8f, 0.75f);
            SetFrictionCurve(wc, false, 0.4f, 1.0f, 0.8f, 0.75f);
        }
    }

    void SetFrictionCurve(WheelCollider wc, bool forward,
                          float extSlip, float extVal, float asymSlip, float asymVal)
    {
        WheelFrictionCurve c = forward ? wc.forwardFriction : wc.sidewaysFriction;
        c.extremumSlip = extSlip;
        c.extremumValue = extVal;
        c.asymptoteSlip = asymSlip;
        c.asymptoteValue = asymVal;
        if (forward) wc.forwardFriction = c;
        else wc.sidewaysFriction = c;
    }

    void FixedUpdate()
    {
        CheckGrounded();
        ApplyAntiRoll();
        ApplyDownforce();

        if (!isGrounded)
            ApplyAirStabilization();
    }


    void CheckGrounded()
    {
        int n = 0;
        if (WC_FrontLeft.isGrounded) n++;
        if (WC_FrontRight.isGrounded) n++;
        if (WC_RearLeft.isGrounded) n++;
        if (WC_RearRight.isGrounded) n++;
        isGrounded = n >= 2;
    }

   

    void ApplyAntiRoll()
    {
        AntiRollBar(WC_FrontLeft, WC_FrontRight, antiRollFront);
        AntiRollBar(WC_RearLeft, WC_RearRight, antiRollRear);
    }

    void AntiRollBar(WheelCollider wL, WheelCollider wR, float force)
    {
        float travelL = GetSuspensionTravel(wL);
        float travelR = GetSuspensionTravel(wR);

   
        float antiRollForce = (travelL - travelR) * force;

       
        if (wL.isGrounded)
            rb.AddForceAtPosition(wL.transform.up * antiRollForce,
                                  wL.transform.position, ForceMode.Force);
        if (wR.isGrounded)
            rb.AddForceAtPosition(wR.transform.up * -antiRollForce,
                                  wR.transform.position, ForceMode.Force);
    }

    float GetSuspensionTravel(WheelCollider wc)
    {
     
        if (!wc.GetGroundHit(out WheelHit hit))
            return 1f;

        float localY = wc.transform.InverseTransformPoint(hit.point).y;
        return Mathf.Clamp01(
            (-localY - wc.radius) / wc.suspensionDistance
        );
    }

    void ApplyDownforce()
    {
        if (!isGrounded) return;

        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        float t = Mathf.Clamp01(speedKmh / downforceMaxSpeed);
        float force = downforceMax * t * t; 

        rb.AddForce(-transform.up * force, ForceMode.Force);
    }

    

    void ApplyAirStabilization()
    {
        
        Vector3 correctionAxis = Vector3.Cross(transform.up, Vector3.up);
        rb.AddTorque(correctionAxis * airStabTorque, ForceMode.Acceleration);

        float h = Input.GetAxis("Horizontal");
        if (Mathf.Abs(h) > 0.1f)
            rb.AddTorque(transform.up * h * airRotForce, ForceMode.Acceleration);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(centerOfMass), 0.12f);
    }
}