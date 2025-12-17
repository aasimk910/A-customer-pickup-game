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

// Set this per agent in Inspector (ex: Yellow=4, Red=3, Grey=3)
[SerializeField] private int assignedCustomerCountOnPickup = 0;

// If false, it will increment by 1 instead of setting to assigned value
[SerializeField] private bool useAssignedCustomerCountOnPickup = true;

[Header("Speed penalty per customer")]
[SerializeField, Range(0f, 0.5f)] private float speedPenaltyPerCustomer = 0.10f; // 10%
[SerializeField, Range(0.1f, 1f)] private float minCustomerSpeedMultiplier = 0.25f; // don't go too slow
[SerializeField] private bool compoundPenalty = true; // true = 0.9^customers



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

    // =========================================================
    // YIELD / AVOIDANCE
    // =========================================================
    [Header("Avoidance / Yielding (Cast-based)")]
    public bool enableAvoidance = true;

    [Tooltip("Put all cars on a dedicated 'Agents' layer and set this mask to that layer.")]
    public LayerMask agentLayerMask = ~0;

    [Header("Detection")]
    [Tooltip("Base how far behind we look for a faster agent coming up.")]
    public float rearRayLength = 5.0f;

    [Tooltip("Base how far ahead we look for another agent.")]
    public float forwardRayLength = 3.0f;

    [Tooltip("Extra forward detection distance per m/s of our speed.")]
    public float forwardRayExtraPerMS = 0.45f;

    [Tooltip("Use SphereCast instead of Raycast (recommended for cars).")]
    public bool useSphereCast = true;

    [Tooltip("Minimum sphere radius for casts.")]
    public float minSphereRadius = 0.35f;

    [Tooltip("Sphere radius scale based on car collider half-width (extents.x).")]
    public float sphereRadiusScale = 0.75f;

    [Header("Front collision avoidance (follow distance)")]
    [Tooltip("Start slowing down when another agent is closer than this.")]
    public float safeFollowDistance = 3.0f;

    [Tooltip("Hard stop if another agent gets closer than this (prevents going through).")]
    public float hardStopDistance = 1.0f;

    [Tooltip("Also slow down if time-to-collision is below this (seconds).")]
    public float ttcSlowSeconds = 1.2f;

    [Tooltip("Hard stop if time-to-collision is below this (seconds).")]
    public float ttcHardStopSeconds = 0.55f;

    [Tooltip("Smallest speed multiplier while following (before hard stop).")]
    [Range(0.05f, 1f)]
    public float minFollowSpeedMultiplier = 0.20f;

    [Tooltip("When braking for a front hazard, also shift right up to this weight (0..1).")]
    [Range(0f, 1f)]
    public float frontAvoidMaxSideWeight = 0.65f;

    [Header("Yielding behaviour (faster car from behind)")]
    [Tooltip("How far we move sideways when yielding (RIGHT).")]
    public float yieldSideOffsetMeters = 1.2f;

    [Tooltip("How fast we snap to the right (sharp).")]
    public float sharpRightSlideSpeed = 10.0f;

    [Tooltip("How fast we merge back to lane (smooth).")]
    public float mergeBackSlideSpeed = 2.5f;

    [Tooltip("Reduce speed while yielding so faster car can pass.")]
    [Range(0.1f, 1f)]
    public float yieldSpeedMultiplier = 0.55f;

    [Tooltip("If another agent is faster than us by this amount, we yield.")]
    public float speedDiffToYield = 0.5f;

    [Tooltip("After moving aside, continue forward this many meters before you're allowed to merge back.")]
    public float forwardAfterSideMeters = 1.5f;

    [Tooltip("Consider the faster car 'passed' when it's this far ahead along our forward direction.")]
    public float passAheadMeters = 2.0f;

    [Header("Debug")]
    public bool debugDraw = true;

    // Internal state
    private float sideWeight = 0f; // 0..1 (0 = lane center, 1 = fully right)
    private bool yieldActive = false;
    private AgentStatsSource yieldingTo = null;
    private Vector3 yieldStartPos;
    private Vector3 yieldForwardDir;

    private Collider cachedCollider;

    void Start()
    {
        stats = GetComponent<AgentStatsSource>();
        cachedCollider = GetComponentInChildren<Collider>();

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

        if (agentMove && ConnectionArray.Count > 0)
        {
            if (currentTarget < 0) currentTarget = 0;
            if (currentTarget >= ConnectionArray.Count) currentTarget = ConnectionArray.Count - 1;

            float moveSpeed = currentSpeed;

// speed reduced by 10% for every customer (compounded)
int customers = (stats != null) ? Mathf.Max(0, stats.packageCount) : 0;

float mult = 1f;
if (compoundPenalty)
    mult = Mathf.Pow(1f - speedPenaltyPerCustomer, customers);   // 0.9^customers
else
    mult = 1f - (speedPenaltyPerCustomer * customers);           // linear option

mult = Mathf.Clamp(mult, minCustomerSpeedMultiplier, 1f);
moveSpeed *= mult;


            currentTargetPos = ConnectionArray[currentTarget].ToNode.transform.position;
            currentTargetPos.y = transform.position.y;

            Vector3 baseDir = currentTargetPos - transform.position;
            baseDir.y = 0f;

            if (baseDir.sqrMagnitude > 0.0001f)
            {
                Vector3 forwardDir = baseDir.normalized;

                Vector3 adjustedTarget = currentTargetPos;
                float speedMult = 1f;

                if (enableAvoidance)
                    ApplyYieldAndAvoidLogic(forwardDir, ref adjustedTarget, ref speedMult, dt);

                Vector3 steerDir = adjustedTarget - transform.position;
                steerDir.y = 0f;

                if (steerDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(steerDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);
                }

                // NOTE: We apply speedMult here.
                transform.position += steerDir.normalized * (moveSpeed * speedMult) * dt;

                float remainingToTrueTarget = (currentTargetPos - transform.position).magnitude;

                if (remainingToTrueTarget < 1f)
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

        Vector3 curPos = transform.position;
        float frameDist = Vector3.Distance(prevPos, curPos);
        totalDistanceTravelled += frameDist;

        currentSpeedValue = (dt > 0f) ? frameDist / dt : 0f;

        // IMPORTANT: keep stats.speedMS updated so yielding works
        if (stats != null)
            stats.speedMS = currentSpeedValue;
    }

    private void ApplyYieldAndAvoidLogic(Vector3 forwardDir, ref Vector3 adjustedTarget, ref float speedMult, float dt)
    {
        // Origins based on collider
        Vector3 center = transform.position + Vector3.up * 0.35f;
        float extentZ = 0.6f;
        float extentX = 0.5f;

        if (cachedCollider != null)
        {
            center = cachedCollider.bounds.center;
            extentZ = Mathf.Max(0.2f, cachedCollider.bounds.extents.z);
            extentX = Mathf.Max(0.2f, cachedCollider.bounds.extents.x);
        }

        // Use measured speed first (stats.speedMS is now updated every frame).
        float mySpeed = Mathf.Max(currentSpeedValue, (stats != null ? stats.speedMS : 0f));

        // Make detection + yielding strong even if inspector values are small.
        float yieldOffset = Mathf.Max(3.5f, yieldSideOffsetMeters);
        float sharpSlide = Mathf.Max(15f, sharpRightSlideSpeed);
        float mergeSlide = Mathf.Max(2.0f, mergeBackSlideSpeed);
        float yieldMult = Mathf.Clamp(yieldSpeedMultiplier, 0.35f, 1f);

        float rearLen = Mathf.Max(10f, rearRayLength) + mySpeed * 0.5f;
        float frontLen = Mathf.Max(2.5f, forwardRayLength + mySpeed * Mathf.Max(0f, forwardRayExtraPerMS));

        Vector3 rearOrigin = center - forwardDir * extentZ;
        Vector3 frontOrigin = center + forwardDir * extentZ;

        float castRadius = useSphereCast ? Mathf.Max(minSphereRadius, extentX * Mathf.Max(0f, sphereRadiusScale)) : 0f;

        Vector3 right = Vector3.Cross(Vector3.up, forwardDir).normalized;

        if (debugDraw)
        {
            Debug.DrawRay(rearOrigin, -forwardDir * rearLen, Color.yellow);
            Debug.DrawRay(frontOrigin, forwardDir * frontLen, Color.cyan);
        }

        // 1) FRONT hazard: prefer "move right + slow" (only stop if right side blocked)
        float frontAvoidWeight = 0f;

        if (TryCastOtherAgent(frontOrigin, forwardDir, frontLen, castRadius, out AgentStatsSource frontAgent, out float frontDist))
        {
            float otherSpeed = (frontAgent != null) ? frontAgent.speedMS : 0f;

            float dirDot = 1f;
            if (frontAgent != null)
            {
                Vector3 otherFwd = frontAgent.transform.forward;
                otherFwd.y = 0f;
                if (otherFwd.sqrMagnitude > 0.0001f)
                    dirDot = Vector3.Dot(otherFwd.normalized, forwardDir.normalized);
            }

            float closingSpeed;
            if (dirDot > 0.2f)
                closingSpeed = mySpeed - otherSpeed;          // same direction
            else if (dirDot < -0.2f)
                closingSpeed = mySpeed + otherSpeed;          // head-on
            else
                closingSpeed = mySpeed;

            closingSpeed = Mathf.Max(0f, closingSpeed);
            float ttc = (closingSpeed > 0.05f) ? (frontDist / closingSpeed) : 999f;

            bool imminent = (frontDist <= hardStopDistance) || (ttc <= ttcHardStopSeconds);

            if (imminent)
            {
                // Try to evade right instead of full stop.
                bool canShiftRight = IsSideClear(center, right, yieldOffset + extentX, castRadius);

                if (canShiftRight)
                {
                    frontAvoidWeight = 1f; // force sharp right
                    speedMult = Mathf.Min(speedMult, Mathf.Max(minFollowSpeedMultiplier, 0.35f));
                }
                else
                {
                    // Only stop if we literally can't move aside.
                    speedMult = 0f;
                    frontAvoidWeight = frontAvoidMaxSideWeight;
                }
            }
            else
            {
                bool needSlow = (frontDist <= safeFollowDistance) || (ttc <= ttcSlowSeconds);
                if (needSlow && closingSpeed > 0.05f)
                {
                    float tDist = Mathf.Clamp01(Mathf.InverseLerp(safeFollowDistance, hardStopDistance, frontDist));
                    float speedByDist = Mathf.Lerp(1f, minFollowSpeedMultiplier, tDist);

                    float tTtc = Mathf.Clamp01(Mathf.InverseLerp(ttcSlowSeconds, ttcHardStopSeconds, ttc));
                    float speedByTtc = Mathf.Lerp(1f, minFollowSpeedMultiplier, tTtc);

                    float target = Mathf.Min(speedByDist, speedByTtc);
                    speedMult = Mathf.Min(speedMult, target);

                    float tSide = Mathf.Max(tDist, tTtc);
                    frontAvoidWeight = Mathf.Clamp01(tSide) * frontAvoidMaxSideWeight;
                }
            }
        }

        // 2) REAR hazard: yield to faster agent from behind (same lane-ish)
        bool rearFastHazard = TryCastOtherAgent(rearOrigin, -forwardDir, rearLen, castRadius, out AgentStatsSource rearAgent, out float rearDist);

        if (rearFastHazard && rearAgent != null)
        {
            Vector3 rel = rearAgent.transform.position - transform.position;
            float behind = Vector3.Dot(rel, forwardDir);                 // negative = behind
            float lateral = Mathf.Abs(Vector3.Dot(rel, right));          // sideways distance

            float otherSpeed = rearAgent.speedMS;

            bool isBehind = behind < -0.1f;
            bool sameLane = lateral < (yieldOffset * 1.25f);
            bool isFaster = otherSpeed > mySpeed + speedDiffToYield;

            if (isBehind && sameLane && isFaster)
            {
                if (!yieldActive || yieldingTo != rearAgent)
                {
                    yieldActive = true;
                    yieldingTo = rearAgent;
                    yieldStartPos = transform.position;

                    yieldForwardDir = forwardDir;
                    yieldForwardDir.y = 0f;
                    if (yieldForwardDir.sqrMagnitude < 0.0001f)
                        yieldForwardDir = transform.forward;
                    yieldForwardDir.Normalize();
                }
            }
        }

        // 3) Yield state machine (sharp right, move a bit forward, then merge back)
        if (yieldActive)
        {
            float forwardMoved = Vector3.Dot(transform.position - yieldStartPos, yieldForwardDir);
            bool passed = HasFasterPassed();

            if (forwardMoved >= forwardAfterSideMeters && passed)
            {
                yieldActive = false;
                yieldingTo = null;
            }

            sideWeight = Mathf.MoveTowards(sideWeight, 1f, sharpSlide * dt);
            speedMult = Mathf.Min(speedMult, yieldMult);
        }
        else
        {
            sideWeight = Mathf.MoveTowards(sideWeight, 0f, mergeSlide * dt);
        }

        // 4) Apply lateral offset (RIGHT)
        float combinedSideWeight = Mathf.Max(sideWeight, frontAvoidWeight);
        Vector3 lateralOffset = right * (yieldOffset * combinedSideWeight);
        adjustedTarget += lateralOffset;
    }

    private bool HasFasterPassed()
    {
        // Unity "fake null" safe-check:
        if (yieldingTo == null) return true;

        Vector3 rel = yieldingTo.transform.position - transform.position;
        float ahead = Vector3.Dot(rel, yieldForwardDir);
        return ahead > passAheadMeters;
    }

    private bool IsSideClear(Vector3 origin, Vector3 sideDir, float distance, float sphereRadius)
    {
        if (distance <= 0.01f) return true;

        RaycastHit[] hits;

        if (useSphereCast && sphereRadius > 0.001f)
            hits = Physics.SphereCastAll(origin, sphereRadius, sideDir, distance, agentLayerMask, QueryTriggerInteraction.Ignore);
        else
            hits = Physics.RaycastAll(origin, sideDir, distance, agentLayerMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0) return true;

        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;

            var a = h.collider.GetComponentInParent<AgentStatsSource>();
            if (a != null && a != stats) return false;
        }

        return true;
    }

    private bool TryCastOtherAgent(
        Vector3 origin,
        Vector3 dir,
        float length,
        float sphereRadius,
        out AgentStatsSource otherAgent,
        out float hitDistance)
    {
        otherAgent = null;
        hitDistance = 0f;

        if (length <= 0f) return false;

        RaycastHit[] hits;

        if (useSphereCast && sphereRadius > 0.001f)
            hits = Physics.SphereCastAll(origin, sphereRadius, dir, length, agentLayerMask, QueryTriggerInteraction.Ignore);
        else
            hits = Physics.RaycastAll(origin, dir, length, agentLayerMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;

            var a = h.collider.GetComponentInParent<AgentStatsSource>();
            if (a != null && a != stats)
            {
                otherAgent = a;
                hitDistance = h.distance;
                return true;
            }
        }

        return false;
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
