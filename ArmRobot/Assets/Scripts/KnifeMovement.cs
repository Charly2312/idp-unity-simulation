using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class SimpleMovement : MonoBehaviour
{
    // === Wound painting ===
    public WoundPainter Painter;           // Add reference to WoundPainter
    public float StampInterval = 0.1f;     // seconds between paint stamps
    public float StampStrength = 1f;
    private float _stampTimer = 0f;
    
    // Manual UV mapping based on skin vertices
    private Vector2 _skinMin = new Vector2(54.69f, -2.26f); // (minX, minZ)
    private Vector2 _skinMax = new Vector2(55.42f, -1.74f); // (maxX, maxZ)
    
    public LayerMask skinLayer;
    public float skinTopLocalY = 0f;

    Rigidbody rb;
    private string portName = "COM3"; // Testing using Arduino
    public int baud = 115200;
    public int readTimeoutMs = 200;

    SerialPort sp;
    Thread reader;
    volatile bool running;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

    public float moveSpeed = 0.1f;
    public float rotationSpeed = 10f;

    // Camera follow properties
    public Transform cameraTransform; // Reference to the camera's transform
    public float cameraFollowSpeed = 2f; // Speed of the camera following the knife
    public Vector3 cameraOffset = new Vector3(0, 1, -3); // Offset from the knife

    // Array cache to store the last 10 inputs
    private float[,] inputCache = new float[10, 6];
    private int cacheIndex = 0; // Keeps track of the current index in the cache

    private bool isManualControl = false; //flag for manual cam control

    private float knifeSharpness = 1.0f;  // A constant to represent the sharpness of the knife
    private float skinTensileStrength = 1.0f;  // A constant for the skin's resistance to cutting (this could be a realistic value)
    private float contactRadius = 0.01f;

    private Vector3 previousPosition = Vector3.zero;
    private Vector3 currentPosition = Vector3.zero;

    bool touchingSkin = false;
    private Vector3 lastPos;
    private Vector3 contactNormal = Vector3.up;

    private bool isStuck = false; // Flag to track if the knife is stuck in the skin

    private Vector3 originPos;
    private Quaternion originRot;

    // mm → m
    private const float MM_TO_M = 0.001f;
    float prevX_mm, prevY_mm, prevZ_mm;
    Vector3 accumMeters; // integrate motion

    [SerializeField] private Vector3 startPos;
    [SerializeField] private Quaternion startRot;

    // --- add these fields ---
    [SerializeField] Transform KnifeTip;         // you already have this
    Transform skinTf;                            // <-- this is what I meant by skinRoot
    MeshCollider skinMesh;                       // your Skin's MeshCollider
    Collider knifeCol;
    public float insideEps = 0.0005f;         // 0.5 mm = considered inside
    public float contactEps = 0.0010f;         // 1.0 mm = near the surface
    public float minPressSpd = 0.02f;           // m/s pressing into the plane required
    bool cutting = false;
    bool ignored = false;
    bool collisionIgnored;
    string lastFlag = "";
    private Vector3 lastTipPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        knifeCol = GetComponent<Collider>();
        lastPos = transform.position;

        var skinGO = GameObject.FindGameObjectWithTag("Skin");
        skinTf = skinGO.transform;                 // this is what we called skinRoot
        skinMesh = skinGO.GetComponent<MeshCollider>();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            startPos = transform.position;
            startRot = transform.rotation;
        }
    }

    void Start()
    {
        if (!KnifeTip) KnifeTip = transform;

        ResetKnifeAndWounds();

        sp = new SerialPort(portName, baud)
        {
            NewLine = "\n",
            ReadTimeout = readTimeoutMs,
            DtrEnable = true,      // Helpful for many USB CDC adapters
            RtsEnable = true,
            Encoding = Encoding.ASCII
        };

        try
        {
            sp.Open();
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true };
            reader.Start();
            Debug.Log($"Opened {sp.PortName} @ {baud}");
        }
        catch (Exception ex)
        {
            Debug.LogError("Open failed: " + ex.Message);
        }
    }

    public void PlayAgain()
    {
        ResetKnifeAndWounds();
    }

    void ResetKnifeAndWounds()
    {
        // Reset transform and cached origins
        transform.position = startPos;
        transform.rotation = startRot;
        originPos = startPos;
        originRot = startRot;

        // Reset motion/state
        accumMeters = Vector3.zero;
        lastPos = transform.position;
        lastTipPos = KnifeTip ? KnifeTip.position : transform.position;

        cutting = false;
        ignored = false;
        isStuck = false;

        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.None;
        }

        // Clear wound mask
        if (Painter != null) Painter.Clear();
    }

    void ReadLoop()
    {
        while (running && sp != null && sp.IsOpen)
        {
            try
            {
                string line = sp.ReadLine();          // blocks until '\n' or timeout
                if (!string.IsNullOrWhiteSpace(line))
                    inbox.Enqueue(line.Trim());
            }
            catch (TimeoutException) { /* ignore */ }
            catch (InvalidOperationException) { break; }     // Port closed
            catch (System.IO.IOException) { /* transient; continue */ }
        }
    }

    void printCache(float[,] arr)
    {
        float x = arr[cacheIndex, 0];
        float y = arr[cacheIndex, 1];
        float z = arr[cacheIndex, 2];
        float yaw = arr[cacheIndex, 3];
        float pitch = arr[cacheIndex, 4];
        float roll = arr[cacheIndex, 5];
        //Debug.Log("x:" + x + ", y:" + y + ", z:" + z + ", yaw:" + yaw + ", pitch:" + pitch + ", roll:" + roll); 
    }

    // Manual camera movement using keyboard or mouse input
    void ManualCameraControl()
    {
        // Camera follows the knife from a fixed position behind it
        //if (cameraTransform != null)
        //{
        //    Vector3 targetPosition = transform.position + cameraOffset;
        //    cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetPosition, cameraFollowSpeed * Time.deltaTime);
        //    cameraTransform.LookAt(transform);  // Make sure the camera is always looking at the knife
        //}

        Debug.Log("Manual camera movement activated...");

        float horizontal = 0f;
        float vertical = 0f;
        float forward = 0f;

        // Use arrow keys or WASD for movement
        if (Input.GetKey(KeyCode.A)) horizontal = -1f; // Left
        if (Input.GetKey(KeyCode.D)) horizontal = 1f;  // Right
        if (Input.GetKey(KeyCode.W)) forward = 1f;    // forward
        if (Input.GetKey(KeyCode.S)) forward = -1f;   // backward
        if (Input.GetKey(KeyCode.UpArrow)) vertical = 1f;   // up
        if (Input.GetKey(KeyCode.DownArrow)) vertical = -1f;   // down

        // Use mouse to control camera rotation (only when manual control is enabled)
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // Move the camera manually using keyboard
        cameraTransform.Translate(new Vector3(horizontal, vertical, forward) * Time.deltaTime * 5f, Space.World);

        // Rotate the camera based on mouse movement
        cameraTransform.Rotate(new Vector3(-mouseY, mouseX, 0) * Time.deltaTime * 100f);

        Debug.Log("Camera movement completed.");
        Debug.Log("Camera Position: " + cameraTransform.position); // Check position
    }

    // Calculate the force to cut through the skin based on the cache data
    public float CalculateCuttingForce()
    {
        // Step 1: Get the knife's velocity from the position data in the cache (use previous and current positions)
        Vector3 velocity = (currentPosition - previousPosition) / Time.deltaTime;

        // Step 2: Calculate the pressure exerted by the knife on the skin
        float pressure = knifeSharpness * velocity.magnitude;  // Magnitude of velocity gives speed

        // Step 3: Estimate the cutting force based on the pressure and a contact area (approximated)
        // Assuming a simple circular contact area (for a knife tip) with a given radius:
        float contactArea = Mathf.PI * Mathf.Pow(0.01f, 2);  // Example: knife tip radius is 0.01 meters (1 cm radius)

        float cuttingForce = pressure * contactArea;

        // Step 4: Compare with skin's tensile strength (if needed)
        if (cuttingForce > skinTensileStrength)
        {
            Debug.Log("Knife can cut through the skin!");
        }
        else
        {
            Debug.Log("Insufficient force to cut through the skin.");
        }

        // Return the calculated cutting force
        return cuttingForce;
    }

    void OnFirstPacket(float x_mm, float y_mm, float z_mm)
    {
        prevX_mm = x_mm; prevY_mm = y_mm; prevZ_mm = z_mm;
    }

    void OnPacket(float x_mm, float y_mm, float z_mm)
    {
        float dx_mm = -(x_mm - prevX_mm);    // >0 ⇒ “right”, <0 ⇒ “left”
        float dy_mm = y_mm - prevY_mm;
        float dz_mm = z_mm - prevZ_mm;

        prevX_mm = x_mm; prevY_mm = y_mm; prevZ_mm = z_mm;

        // integrate as small steps
        accumMeters += new Vector3(dy_mm, dz_mm, dx_mm) * MM_TO_M;
        transform.position = originPos + accumMeters;
    }

    void serialMove()
    {
        int n = 0;
        while (n++ < 5 && inbox.TryDequeue(out var msg))
        {
            // Debug: log the incoming message
            Debug.Log("Received message: " + msg);

            // Remove the square brackets and split by comma
            string[] parts = msg.Trim('[', ']').Split(',');

            // Add the parsed parts array to the cache

            // Check if there are exactly 7 values (x, y, z, yaw, pitch, roll, moveCam)
            if (parts.Length == 7)
            {
                try
                {
                    if (float.Parse(parts[6].Trim(), CultureInfo.InvariantCulture) == 1.0f)
                    {
                        ManualCameraControl();
                    }
                    else //parts[6] == 0
                    {
                        // Convert each part to float using InvariantCulture to avoid issues with locale-specific number formats
                        float x = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 0] = x;

                        float y = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 1] = y;

                        float z = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 2] = z;

                        float yaw = float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 3] = yaw;

                        float pitch = float.Parse(parts[4].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 4] = pitch;

                        float roll = float.Parse(parts[5].Trim(), CultureInfo.InvariantCulture);
                        inputCache[cacheIndex, 5] = roll;

                        // incoming in millimetres
                        float x_mm = x;
                        float y_mm = y;
                        float z_mm = z;

                        OnPacket(x_mm, y_mm, z_mm);

                        //// convert to meters (and remap axes if needed)
                        //Vector3 offsPosMeters = new Vector3(x_mm, y_mm, z_mm) * MM_TO_M;
                        //// e.g., if your sensor’s Z is Unity’s Y, do: new Vector3(x_mm, z_mm, y_mm) * MM_TO_M;

                        //Vector3 offsEulerDeg = new Vector3(pitch, yaw, roll); // if these are in degrees
                        //                                                      // If they’re radians: offsEulerDeg = new Vector3(pitch, yaw, roll) * Mathf.Rad2Deg;

                        //Vector3 targetPos = originPos + offsPosMeters;
                        //Quaternion targetRot = originRot * Quaternion.Euler(offsEulerDeg);

                        //// If using Rigidbody, prefer MovePosition/MoveRotation in FixedUpdate
                        //transform.position = targetPos;
                        //transform.rotation = targetRot;

                        //// Optional smoothing
                        //// transform.position = Vector3.Lerp(transform.position, targetPos, 10f * Time.deltaTime);
                        //// transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.deltaTime);

                        //printCache(inputCache);

                        //// Store the current and previous positions from the input cache (simplified for example)
                        //previousPosition = currentPosition;
                        //currentPosition = new Vector3(inputCache[cacheIndex, 0], inputCache[cacheIndex, 1], inputCache[cacheIndex, 2]);

                        //// Calculate cutting force
                        //float force = CalculateCuttingForce();

                        //// Check if the knife should get stuck in its orientation or start moving into the skin
                        //if (force > 0f && !isStuck)
                        //{
                        //    // Lock the knife's rotation and slowly apply movement into the skin
                        //    isStuck = true;
                        //    rb.constraints = RigidbodyConstraints.FreezeRotation;  // Freeze rotation to stop the knife from moving further
                        //    Debug.Log("Knife is stuck in the skin, now applying cutting force.");

                        //    // Gradually move the knife into the skin by applying a small force based on cutting force
                        //    Vector3 movementDirection = contactNormal * (force * 0.01f); // Small force application
                        //    rb.MovePosition(transform.position + movementDirection);
                        //}
                    }
                }
                catch (FormatException e)
                {
                    Debug.LogError("Error parsing message: " + msg + ". Exception: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning("Invalid message format. Expected 7 values, got " + parts.Length);
            }
        }
    }

    void manualMove()
    {
        // --- Position movement ---
        float moveX = 0f;
        float moveZ = 0f;
        float moveY = 0f;

        if (Input.GetKey(KeyCode.A)) moveX = -0.5f;   // Left
        if (Input.GetKey(KeyCode.D)) moveX = 0.5f;    // Right
        if (Input.GetKey(KeyCode.W)) moveZ = 0.5f;    // Forward
        if (Input.GetKey(KeyCode.S)) moveZ = -0.5f;   // Backward
        if (Input.GetKey(KeyCode.E)) moveY = 0.5f;    // Up
        if (Input.GetKey(KeyCode.Q)) moveY = -0.5f;   // Down

        Vector3 move = new Vector3(moveX, moveY, moveZ);
        transform.Translate(move * moveSpeed * Time.deltaTime, Space.World);

        // --- Rotation ---
        float yaw = 0f, pitch = 0f, roll = 0f;

        if (Input.GetKey(KeyCode.J)) yaw = -1f;     // Yaw left
        if (Input.GetKey(KeyCode.L)) yaw = 1f;      // Yaw right
        if (Input.GetKey(KeyCode.I)) pitch = -1f;   // Pitch up
        if (Input.GetKey(KeyCode.K)) pitch = 1f;    // Pitch down
        if (Input.GetKey(KeyCode.U)) roll = -1f;    // Roll left
        if (Input.GetKey(KeyCode.O)) roll = 1f;     // Roll right

        Vector3 rotation = new Vector3(pitch, yaw, roll);
        transform.Rotate(rotation * rotationSpeed * Time.deltaTime, Space.Self);
    }

    //void PaintCutIfTouching()
    //{
    //    if (!knifeTip) return;

    //    // Ray starts at the knife tip and goes in the direction the blade points into the skin.
    //    // If your tip’s local Up points out of the blade, use -knifeTip.up (as below).
    //    Ray ray = new Ray(knifeTip.position, -knifeTip.up);

    //    // Cast a short distance (tweak 0.1f to your scene scale)
    //    if (!Physics.Raycast(ray, out RaycastHit hit, 0.1f, skinLayer)) return;

    //    var painter = hit.collider.GetComponent<WoundPainter>();
    //    if (!painter) return;

    //    // For a quick test, force a visible dab:
    //    // painter.PaintUV(hit.textureCoord, painter.maxDepthMm);
    //    // return;

    //    // Simple depth estimate along the surface normal
    //    float depthM = Mathf.Max(0f, Vector3.Dot(hit.point - knifeTip.position, hit.normal) * -1f);
    //    float depthMm = depthM / MM_TO_M;   // you already have MM_TO_M = 0.001f

    //    if (depthMm > 0f)
    //        painter.PaintUV(hit.textureCoord, depthMm);
    //}

    // --- Signed distance helpers (Plane defined by Skin.up through Skin.position) ---
    float SignedToPlane()
    {
        //Plane pl = new Plane(skinTf.up, skinTf.position);
        //return pl.GetDistanceToPoint(KnifeTip.position); // + above, - below the plane
        // point *on* the surface plane in world coords
        Vector3 planePoint = skinTf.TransformPoint(new Vector3(0f, skinTopLocalY, 0f));
        Plane pl = new Plane(skinTf.up, planePoint);
        return pl.GetDistanceToPoint(KnifeTip ? KnifeTip.position : transform.position); // + above, - below
    }
    float DepthBelowPlane()
    {
        float s = SignedToPlane();
        return Mathf.Max(0f, -s); // positive only when the tip is below the plane
    }

    void SendFlag(string flag, float depth)
    {
        if (flag == lastFlag) return;
        lastFlag = flag;
        try { if (sp != null && sp.IsOpen) sp.WriteLine($"{flag},{depth:F4}"); } catch { }
        Debug.Log($"{flag} depth={depth * 1000f:F1} mm");
    }


    void Update()
    {
        //serialMove();
        manualMove();
        // depth and approach
        // Tip-based depth/velocity
        float signed = SignedToPlane();
        float depth = Mathf.Max(0f, -signed);

        Vector3 vTip = (KnifeTip.position - lastTipPos) / Mathf.Max(Time.deltaTime, 1e-6f);
        lastTipPos = KnifeTip.position;
        float pressSpeed = Mathf.Max(0f, Vector3.Dot(vTip, -skinTf.up));

        // Consider a tiny band around the surface and the "inside" state
        bool tipInside = (signed < 0f); // as soon as the tip crosses the plane
        bool nearSurfaceAndPressing = (signed >= -contactEps && signed <= contactEps && pressSpeed > minPressSpd);

        if (!cutting && (TipInsideMesh() || nearSurfaceAndPressing))
        {
            cutting = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            if (skinMesh && !skinMesh.isTrigger && !ignored && knifeCol)
            {
                Physics.IgnoreCollision(knifeCol, skinMesh, true); // let blade pass through
                ignored = true;
            }
            Debug.Log("cutting skin");
            TrySend($"cutting,{depth:F4}");
        }
        else if (cutting && !TipInsideMesh() && !nearSurfaceAndPressing)
        {
            cutting = false;
            rb.constraints = RigidbodyConstraints.None;
            if (ignored && skinMesh && knifeCol)
            {
                Physics.IgnoreCollision(knifeCol, skinMesh, false);
                ignored = false;
            }
            Debug.Log("out of skin");
            TrySend($"out,{depth:F4}");
        }
        else
        {
            Debug.Log((cutting ? "cutting skin" : "out of skin") + $" depth={depth * 1000f:F1} mm");
        }


        // paint the wound if the tip is touching the skin
        //PaintCutIfTouching();

        // Calculate velocity and cutting force
        //Vector3 velocity = (transform.position - lastPos) / Time.deltaTime;
        //lastPos = transform.position;

        //if (touchingSkin && !isStuck)
        //{
        //    EvaluateCuttingForce(velocity, contactNormal);
        //}
    }

    bool TipInsideMesh()
    {
        // If the closest point ON the collider to the tip is the tip itself,
        // the tip is inside (within a small epsilon).
        Vector3 cp = skinMesh.ClosestPoint(KnifeTip.position);
        return (cp - KnifeTip.position).sqrMagnitude < 1e-10f;
    }

    // Approximate penetration depth along the skin's normal:
    float TipDepthApprox()
    {
        if (!KnifeTip || !skinMesh) return 0f;

        Vector3 tip = KnifeTip.position;
        Vector3 cp = skinMesh.ClosestPoint(tip);

        // outside ⇒ ClosestPoint != tip
        bool inside = (cp - tip).sqrMagnitude < 1e-12f;
        if (!inside) return 0f;

        // inside ⇒ use plane for a cheap magnitude (works well if your skin surface is locally planar)
        return Mathf.Max(0f, -SignedToPlane());
    }


    void OnDisable() => ClosePort();
    void OnApplicationQuit() => ClosePort();

    void ClosePort()
    {
        running = false;
        try { reader?.Join(300); } catch { }
        try { if (sp?.IsOpen == true) sp.Close(); } catch { }
        try { sp?.Dispose(); } catch { }
        reader = null; sp = null;
    }

    void EvaluateCuttingForce(Vector3 velocity, Vector3 normal)
    {
        // Only the component pressing into the skin matters
        float normalSpeed = Vector3.Dot(velocity, -normal); // > 0 if moving into the skin
        if (normalSpeed < 0f) normalSpeed = 0f;

        // Calculate pressure from knife sharpness and velocity
        float pressure = knifeSharpness * normalSpeed;  // Magnitude of velocity gives speed

        // Assuming a simple circular contact area (for a knife tip) with a given radius:
        float contactArea = Mathf.PI * Mathf.Pow(contactRadius, 2); // Knife tip radius (1 cm for example)

        // Calculate the cutting force
        float cuttingForce = pressure * contactArea + 1.0f;

        // Check if the knife should get stuck in its orientation or start moving into the skin
        if (cuttingForce > 0f && !isStuck)
        {
            // Lock the knife's rotation to stop it from moving further while stuck
            isStuck = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;  // Freeze rotation

            Debug.Log("Knife is stuck in the skin, now applying cutting force.");

            // Gradually move the knife into the skin based on the calculated cutting force
            // Use a small multiplier to avoid applying too much force at once
            Vector3 movementDirection = normal * (cuttingForce * 0.11f); // Apply a small force to move the knife
            rb.MovePosition(transform.position + movementDirection);

            Debug.Log($"Applied movement to knife: {movementDirection}");
        }

        // Check if the force is greater than the skin's tensile strength
        if (cuttingForce > skinTensileStrength)
        {
            Debug.Log($"Knife can cut through the skin! F={cuttingForce:F4}");
            // TODO: invoke skin behavior here, e.g. Skin.TakeCut()
        }
        else
        {
            Debug.Log($"Insufficient force. F={cuttingForce:F4}");
        }
    }

    void TrySend(string s)
    {
        try { if (sp != null && sp.IsOpen) sp.WriteLine(s); } catch { }
    }

    // --- OR Collision style (Skin trigger OFF, both colliders non-trigger) ---
    void OnCollisionEnter(Collision col)
    {
        if (col.collider.CompareTag("Skin"))
        {
            touchingSkin = true;
            if (col.contactCount > 0) contactNormal = col.GetContact(0).normal;
        }
    }

    void OnCollisionStay(Collision col)
    {
        if (col.collider.CompareTag("Skin") && col.contactCount > 0)
        {
            contactNormal = col.GetContact(0).normal;
        }
    }

    void OnCollisionExit(Collision col)
    {
        if (col.collider.CompareTag("Skin")) touchingSkin = false;
    }
}