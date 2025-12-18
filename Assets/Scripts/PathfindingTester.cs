using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindingTester : MonoBehaviour
{
    private AStarManager AStarManager = new AStarManager();
    private List<GameObject> Waypoints = new List<GameObject>();
    private List<Connection> ConnectionArray = new List<Connection>();

    [SerializeField] private GameObject start;
    [SerializeField] private GameObject pickup;
    [SerializeField] private GameObject end;

    [SerializeField] private GameObject customer;

    [SerializeField] private Transform passengerSeat;
    [SerializeField] private float seatMoveDuration = 1.5f;

    private Vector3 OffSet = new Vector3(0, 0.3f, 0);

    [SerializeField] private float currentSpeed = 8f;
    [SerializeField] private float turnSpeed = 5f;
    [SerializeField] private bool agentMove = true;
    [SerializeField] private float waitAtPickupSeconds = 3f;

    [Header("Customer Count (Live Status)")]
    [SerializeField] private bool resetCustomerCountOnStart = true;
    [SerializeField] private int assignedCustomerCountOnPickup = 0;
    [SerializeField] private bool useAssignedCustomerCountOnPickup = true;

    [Header("Speed penalty per customer")]
    [SerializeField, Range(0f, 0.5f)] private float speedPenaltyPerCustomer = 0.10f; // 10%
    [SerializeField, Range(0.1f, 1f)] private float minCustomerSpeedMultiplier = 0.25f;
    [SerializeField] private bool compoundPenalty = true;

    private int currentTarget = 0;
    private Vector3 currentTargetPos;

    private int startToPickupCount = 0;
    private int pickupToEndCount = 0;
    private int endToStartCount = 0;

    private bool hasPassenger = false;

    private float elapsedTime = 0f;
    private float totalDistanceTravelled = 0f;
    private float currentSpeedValue = 0f;
    private bool timerRunning = true;

    private string statusText = "Heading to pickup";

    private AgentStatsSource stats;
    private Rigidbody cachedRigidbody;

    // =========================================================
    // PRE-COLLISION YIELD (RAYCAST) - SLOWER MOVES RIGHT & STOPS
    // =========================================================
    [Header("Pre-Collision Yield (Raycast)")]
    public bool enablePreCollisionYield = true;

    [Tooltip("Set this to your Agents layer only (recommended).")]
    public LayerMask agentLayerMask = ~0;

    [Tooltip("How far behind to raycast to detect a faster agent approaching.")]
    public float rearRayDistance = 7f;

    [Tooltip("Faster agent must exceed us by this many m/s to trigger yield.")]
    public float speedDiffToYield = 0.5f;

    [Tooltip("World +X shift when yielding (move right).")]
    public float yieldShiftRightX = 3.5f;

    [Tooltip("How fast (m/s) we slide right.")]
    public float yieldShiftSpeed = 25f;

    [Tooltip("Consider faster car passed when it's this far ahead (meters).")]
    public float yieldPassAheadMeters = 2.0f;

    [Tooltip("Safety timeout so we don't get stuck waiting.")]
    public float yieldMaxWaitSeconds = 6f;

    [Tooltip("Cooldown to avoid re-triggering instantly.")]
    public float yieldCooldownSeconds = 0.6f;

    private bool preYieldActive = false;
    private AgentStatsSource preYieldFasterAgent = null;
    private float yieldCooldownUntil = 0f;
    private float yieldHoldY = 0f;
    private float yieldHoldZ = 0f;
    private Vector3 yieldForwardDir = Vector3.forward;

    // =========================================================
    // COLLISION RECOVERY (optional safety fallback)
    // =========================================================
    [Header("Collision Recovery (On actual collision)")]
    public bool enableCollisionRecovery = true;

    [Tooltip("World +X shift applied to the slower car on collision.")]
    public float collisionShiftRightX = 3.5f;

    [Tooltip("How fast (m/s) we slide right on collision.")]
    public float collisionShiftSpeed = 20f;

    [Tooltip("Consider the faster car 'passed' when it's this far ahead (meters).")]
    public float collisionPassAhead = 2.0f;

    [Tooltip("After the faster car passes, move forward this many meters before resuming path following.")]
    public float collisionForwardAfterPass = 1.5f;

    [Tooltip("How fast (m/s) we move forward during the recovery step.")]
    public float collisionForwardSpeed = 6f;

    [Tooltip("Safety timeout so we don't get stuck if the other car disappears.")]
    public float collisionMaxWaitSeconds = 6f;

    [Tooltip("Cooldown to avoid retriggering repeatedly on the same contact.")]
    public float collisionCooldownSeconds = 0.5f;

    private bool collisionRecoveryActive = false;
    private AgentStatsSource collisionFasterAgent = null;
    private float collisionCooldownUntil = 0f;
    private float collisionLaneY = 0f;
    private float collisionHoldZ = 0f;
    private Vector3 collisionForwardDir = Vector3.forward;

    void Start()
    {
        stats = GetComponent<AgentStatsSource>();
        cachedRigidbody = GetComponent<Rigidbody>();
        if (cachedRigidbody == null) cachedRigidbody = GetComponentInChildren<Rigidbody>();

        if (stats != null)
        {
            if (string.IsNullOrWhiteSpace(stats.agentName) || stats.agentName.Trim().Equals("Agent"))
                stats.agentName = transform.root.name;

            stats.deliveryStatus = "Heading to pickup";

            if (resetCustomerCountOnStart)
                stats.packageCount = 0;
        }

        if (start == null || pickup == null || end == null)
        {
            Debug.Log("Start, pickup or end waypoints are not assigned.");
            return;
        }

        if (passengerSeat == null)
            Debug.LogWarning("Passenger seat is not assigned. Customer will still disappear at pickup.");

        if (start.GetComponent<VisGraphWaypointManager>() == null) { Debug.Log("Start is not a waypoint."); return; }
        if (pickup.GetComponent<VisGraphWaypointManager>() == null) { Debug.Log("Pickup is not a waypoint."); return; }
        if (end.GetComponent<VisGraphWaypointManager>() == null) { Debug.Log("End is not a waypoint."); return; }

        transform.position = start.transform.position;

        GameObject[] gameObjectsWithWaypointTag = GameObject.FindGameObjectsWithTag("Waypoint");
        foreach (GameObject waypoint in gameObjectsWithWaypointTag)
        {
            if (waypoint.GetComponent<VisGraphWaypointManager>())
                Waypoints.Add(waypoint);
        }

        foreach (GameObject waypoint in Waypoints)
        {
            VisGraphWaypointManager tmpWaypointMan = waypoint.GetComponent<VisGraphWaypointManager>();
            foreach (VisGraphConnection aVisGraphConnection in tmpWaypointMan.Connections)
            {
                if (aVisGraphConnection.ToNode != null)
                {
                    Connection aConnection = new Connection();
                    aConnection.FromNode = waypoint;
                    aConnection.ToNode = aVisGraphConnection.ToNode;
                    AStarManager.AddConnection(aConnection);
                }
                else
                {
                    Debug.Log("Warning, " + waypoint.name + " has a missing to node for a connection!");
                }
            }
        }

        List<Connection> pathStartToPickup = AStarManager.PathfindAStar(start, pickup);
        List<Connection> pathPickupToEnd = AStarManager.PathfindAStar(pickup, end);
        List<Connection> pathEndToStart = AStarManager.PathfindAStar(end, start);

        ConnectionArray.Clear();
        ConnectionArray.AddRange(pathStartToPickup);
        ConnectionArray.AddRange(pathPickupToEnd);
        ConnectionArray.AddRange(pathEndToStart);

        if (ConnectionArray.Count == 0)
        {
            Debug.Log("Warning, no path for the taxi route.");
            return;
        }

        startToPickupCount = pathStartToPickup.Count;
        pickupToEndCount = pathPickupToEnd.Count;
        endToStartCount = pathEndToStart.Count;

        currentTarget = 0;
        hasPassenger = false;
        statusText = "Heading to pickup";
        timerRunning = true;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        foreach (Connection aConnection in ConnectionArray)
        {
            Gizmos.DrawLine((aConnection.FromNode.transform.position + OffSet),
                            (aConnection.ToNode.transform.position + OffSet));
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (timerRunning)
            elapsedTime += dt;

        Vector3 prevPos = transform.position;

        // Always keep measured speed updated (used for yield decision by others)
        // (We set it at the end after movement too.)

        if (agentMove && ConnectionArray.Count > 0)
        {
            if (currentTarget < 0) currentTarget = 0;
            if (currentTarget >= ConnectionArray.Count) currentTarget = ConnectionArray.Count - 1;

            // effective speed after customer penalty
            float moveSpeed = currentSpeed;

            int customers = (stats != null) ? Mathf.Max(0, stats.packageCount) : 0;
            float mult = compoundPenalty
                ? Mathf.Pow(1f - speedPenaltyPerCustomer, customers)
                : (1f - (speedPenaltyPerCustomer * customers));

            mult = Mathf.Clamp(mult, minCustomerSpeedMultiplier, 1f);
            moveSpeed *= mult;

            currentTargetPos = ConnectionArray[currentTarget].ToNode.transform.position;
            currentTargetPos.y = transform.position.y;

            Vector3 dir = currentTargetPos - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Vector3 forwardDir = dir.normalized;

                // ===== PRE-COLLISION RAYCAST YIELD (ONLY IF NOT ALREADY YIELDING) =====
                if (enablePreCollisionYield && !preYieldActive && !collisionRecoveryActive && Time.time >= yieldCooldownUntil)
                {
                    if (TryDetectFasterAgentFromBehind(forwardDir, moveSpeed, out AgentStatsSource fasterAgent))
                    {
                        StartCoroutine(PreCollisionYieldRoutine(fasterAgent, forwardDir));
                        // Skip normal movement this frame.
                        UpdateMeasuredSpeed(prevPos, dt);
                        return;
                    }
                }

                Quaternion targetRot = Quaternion.LookRotation(forwardDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);

                transform.position += forwardDir * moveSpeed * dt;

                float remainingToTarget = (currentTargetPos - transform.position).magnitude;

                if (remainingToTarget < 1f)
                {
                    currentTarget++;

                    if (!hasPassenger && currentTarget == startToPickupCount)
                    {
                        hasPassenger = true;
                        StartCoroutine(PickupSequence());
                    }

                    if (currentTarget >= ConnectionArray.Count)
                    {
                        currentTarget = ConnectionArray.Count - 1;
                        agentMove = false;
                        statusText = "Returned to start";
                        timerRunning = false;

                        if (stats != null)
                            stats.deliveryStatus = "Returned to start";
                    }
                }
            }
        }

        UpdateMeasuredSpeed(prevPos, dt);
    }

    private void UpdateMeasuredSpeed(Vector3 prevPos, float dt)
    {
        Vector3 curPos = transform.position;
        float frameDist = Vector3.Distance(prevPos, curPos);
        totalDistanceTravelled += frameDist;

        currentSpeedValue = (dt > 0f) ? frameDist / dt : 0f;

        if (stats != null)
            stats.speedMS = currentSpeedValue;
    }

    private bool TryDetectFasterAgentFromBehind(Vector3 forwardDir, float myPlannedSpeed, out AgentStatsSource fasterAgent)
    {
        fasterAgent = null;

        // Raycast from slightly above center backward
        Vector3 origin = transform.position + Vector3.up * 0.35f;
        Vector3 backDir = -forwardDir;

        RaycastHit[] hits = Physics.RaycastAll(origin, backDir, rearRayDistance, agentLayerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        float mySpeedForCompare = Mathf.Max(myPlannedSpeed, (stats != null ? stats.speedMS : 0f));

        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;

            AgentStatsSource other = h.collider.GetComponentInParent<AgentStatsSource>();
            if (other == null || other == stats) continue;

            // ensure truly behind (not some side collider weirdness)
            Vector3 rel = other.transform.position - transform.position;
            float behind = Vector3.Dot(rel, forwardDir); // negative => behind
            if (behind >= -0.1f) continue;

            float otherSpeed = other.speedMS;
            if (otherSpeed > mySpeedForCompare + speedDiffToYield)
            {
                fasterAgent = other;
                return true;
            }
        }

        return false;
    }

    private IEnumerator PreCollisionYieldRoutine(AgentStatsSource fasterAgent, Vector3 forwardDir)
    {
        preYieldActive = true;
        preYieldFasterAgent = fasterAgent;
        yieldCooldownUntil = Time.time + yieldCooldownSeconds;

        // Stop pathing immediately (we will reposition manually)
        agentMove = false;

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        yieldHoldY = transform.position.y;
        yieldHoldZ = transform.position.z;

        yieldForwardDir = forwardDir;
        yieldForwardDir.y = 0f;
        if (yieldForwardDir.sqrMagnitude < 0.0001f)
            yieldForwardDir = transform.forward;
        yieldForwardDir.Normalize();

        // 1) Immediately shift to +X (right)
        float targetX = transform.position.x + yieldShiftRightX;

        while (Mathf.Abs(transform.position.x - targetX) > 0.01f)
        {
            float newX = Mathf.MoveTowards(transform.position.x, targetX, yieldShiftSpeed * Time.deltaTime);
            transform.position = new Vector3(newX, yieldHoldY, yieldHoldZ);

            if (cachedRigidbody != null)
            {
                cachedRigidbody.velocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            yield return null;
        }

        // 2) STOP and wait until the faster car is ahead
        float t = 0f;
        while (preYieldFasterAgent != null && t < yieldMaxWaitSeconds)
        {
            // keep us fixed in the yielded position (stop)
            transform.position = new Vector3(targetX, yieldHoldY, yieldHoldZ);

            Vector3 rel = preYieldFasterAgent.transform.position - transform.position;
            float ahead = Vector3.Dot(rel, yieldForwardDir);
            if (ahead > yieldPassAheadMeters)
                break;

            if (cachedRigidbody != null)
            {
                cachedRigidbody.velocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            t += Time.deltaTime;
            yield return null;
        }

        // 3) Resume normal pathing (move again)
        preYieldFasterAgent = null;
        preYieldActive = false;
        agentMove = true;
    }

    // =========================================================
    // COLLISION (post-impact) RESPONSE (fallback)
    // =========================================================
    private void OnCollisionEnter(Collision collision)
    {
        if (!enableCollisionRecovery) return;
        if (collisionRecoveryActive) return;
        if (preYieldActive) return;
        if (Time.time < collisionCooldownUntil) return;
        if (!agentMove) return;

        AgentStatsSource otherAgent = collision.collider.GetComponentInParent<AgentStatsSource>();
        if (otherAgent == null || otherAgent == stats) return;

        TryStartCollisionRecovery(otherAgent);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!enableCollisionRecovery) return;
        if (collisionRecoveryActive) return;
        if (preYieldActive) return;
        if (Time.time < collisionCooldownUntil) return;
        if (!agentMove) return;

        AgentStatsSource otherAgent = other.GetComponentInParent<AgentStatsSource>();
        if (otherAgent == null || otherAgent == stats) return;

        TryStartCollisionRecovery(otherAgent);
    }

    private void TryStartCollisionRecovery(AgentStatsSource otherAgent)
    {
        float mySpd = (stats != null) ? stats.speedMS : currentSpeedValue;
        float otherSpd = otherAgent.speedMS;

        const float eps = 0.05f;
        bool iAmSlower;

        if (mySpd < otherSpd - eps) iAmSlower = true;
        else if (otherSpd < mySpd - eps) iAmSlower = false;
        else iAmSlower = (gameObject.GetInstanceID() > otherAgent.gameObject.GetInstanceID());

        if (!iAmSlower) return;

        collisionFasterAgent = otherAgent;
        StartCoroutine(CollisionRecoveryRoutine());
    }

    private IEnumerator CollisionRecoveryRoutine()
    {
        collisionRecoveryActive = true;
        collisionCooldownUntil = Time.time + collisionCooldownSeconds;

        agentMove = false;

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        collisionLaneY = transform.position.y;
        collisionHoldZ = transform.position.z;

        collisionForwardDir = transform.forward;
        collisionForwardDir.y = 0f;
        if (collisionForwardDir.sqrMagnitude < 0.0001f)
            collisionForwardDir = Vector3.forward;
        collisionForwardDir.Normalize();

        float targetX = transform.position.x + collisionShiftRightX;

        while (Mathf.Abs(transform.position.x - targetX) > 0.01f)
        {
            float newX = Mathf.MoveTowards(transform.position.x, targetX, collisionShiftSpeed * Time.deltaTime);
            transform.position = new Vector3(newX, collisionLaneY, collisionHoldZ);

            if (cachedRigidbody != null)
            {
                cachedRigidbody.velocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            yield return null;
        }

        float t = 0f;
        while (collisionFasterAgent != null && t < collisionMaxWaitSeconds)
        {
            transform.position = new Vector3(targetX, collisionLaneY, collisionHoldZ);

            Vector3 rel = collisionFasterAgent.transform.position - transform.position;
            float ahead = Vector3.Dot(rel, collisionForwardDir);
            if (ahead > collisionPassAhead)
                break;

            if (cachedRigidbody != null)
            {
                cachedRigidbody.velocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            t += Time.deltaTime;
            yield return null;
        }

        Vector3 startPos = transform.position;
        Vector3 forwardTarget = startPos + collisionForwardDir * collisionForwardAfterPass;
        forwardTarget.y = collisionLaneY;

        while ((forwardTarget - transform.position).sqrMagnitude > 0.01f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, forwardTarget, collisionForwardSpeed * Time.deltaTime);
            next.y = collisionLaneY;
            transform.position = next;

            if (cachedRigidbody != null)
            {
                cachedRigidbody.velocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            yield return null;
        }

        collisionFasterAgent = null;
        collisionRecoveryActive = false;
        agentMove = true;
    }

    // =========================================================
    // CUSTOMER PICKUP
    // =========================================================
    private IEnumerator MoveCustomerToSeat()
    {
        if (customer == null || passengerSeat == null)
        {
            if (customer != null) customer.SetActive(false);
            yield break;
        }

        Transform cust = customer.transform;

        Vector3 startPos = cust.position;
        Quaternion startRot = cust.rotation;

        Vector3 endPos = passengerSeat.position;
        Quaternion endRot = passengerSeat.rotation;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / seatMoveDuration;
            float smoothT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            cust.position = Vector3.Lerp(startPos, endPos, smoothT);
            cust.rotation = Quaternion.Slerp(startRot, endRot, smoothT);

            yield return null;
        }

        cust.SetParent(this.transform);
        customer.SetActive(true);
    }

    private IEnumerator PickupSequence()
    {
        agentMove = false;
        statusText = "Loading passenger";

        if (stats != null)
        {
            if (useAssignedCustomerCountOnPickup && assignedCustomerCountOnPickup > 0)
                stats.packageCount = assignedCustomerCountOnPickup;
            else
                stats.packageCount += 1;
        }

        yield return new WaitForSeconds(2f);
        yield return StartCoroutine(MoveCustomerToSeat());

        float remaining = Mathf.Max(0f, waitAtPickupSeconds - 2f);
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        statusText = "Passenger picked up";
        if (stats != null)
            stats.deliveryStatus = "Passenger picked up";

        agentMove = true;
    }
}
