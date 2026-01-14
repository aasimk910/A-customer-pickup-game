using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindingTester : MonoBehaviour
{
    private AStarManager AStarManager = new AStarManager();
    private List<GameObject> Waypoints = new List<GameObject>();
    private List<Connection> ConnectionArray = new List<Connection>();

    [SerializeField] public GameObject start;
    [SerializeField] public GameObject pickup;
    [SerializeField] public GameObject end;

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
    
    // Data inherited from ACOTester when switching
    private float inheritedSpeed = 0f;
    private int inheritedPackageCount = 0;
    private bool useInheritedSpeed = false;

    private float elapsedTime = 0f;
    private float totalDistanceTravelled = 0f;
    private float currentSpeedValue = 0f;
    private bool timerRunning = true;

    private string statusText = "Returning to start using Pathfinding Tester";

    private AgentStatsSource stats;
    private Rigidbody cachedRigidbody;

    // Track if currently picking up a customer
    private bool isPickingUp = false;
    public bool IsPickingUp => isPickingUp;
    
    // Track if waiting for another agent's pickup
    private bool isWaitingForPickup = false;

    // Collision Avoidance State - Raycast-based
    private bool isYielding = false;
    private Vector3 yieldTargetPosition;
    private float yieldTimer = 0f;
    private Transform detectedAgent = null;
    
    [Header("Collision Avoidance Settings")]
    [SerializeField] private float raycastDistance = 20f;        // How far to detect in front and rear
    [SerializeField] private float raycastWidth = 8f;            // How far to detect on sides (left/right)
    [SerializeField] private float sideStepDistance = 5f;        // How far to move aside immediately
    [SerializeField] private float yieldWaitTime = 3f;           // How long to wait after stepping aside
    [SerializeField] private float safeDistance = 8f;            // Min distance to maintain
    [SerializeField] private float yieldSpeedMultiplier = 0f;    // COMPLETE STOP when yielding
    private int agentLayer;                                      // Agent layer for detection
    
    public bool IsYielding => isYielding;              // Public for HUD/debugging

    // Track if already initialized
    private bool isInitialized = false;

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        // Re-initialize when enabled (for ACO -> A* switch)
        if (isInitialized)
        {
            // Already ran Start once, need to reinitialize for new waypoints
            ReInitializeForReturn();
        }
    }

    /// <summary>
    /// Called by ACOTester to reinitialize for return journey.
    /// </summary>
    public void InitializeForReturn()
    {
        Debug.Log($"[PathfindingTester] {gameObject.name}: InitializeForReturn called. Start={start?.name}, End={end?.name}");
        ReInitializeForReturn();
    }

    /// <summary>
    /// Set the inherited speed and package count from ACOTester when switching.
    /// </summary>
    public void SetInheritedSpeed(float speed, int packageCount = 0)
    {
        inheritedSpeed = speed;
        inheritedPackageCount = packageCount;
        useInheritedSpeed = true;
        currentSpeed = speed;
        currentSpeedValue = speed;
        
        // Ensure stats is assigned
        if (stats == null)
            stats = GetComponent<AgentStatsSource>();
        
        // Update stats immediately so HUD shows correct values
        if (stats != null)
        {
            stats.speedMS = speed;
            stats.packageCount = packageCount;
        }
        
        Debug.Log($"[PathfindingTester] {gameObject.name}: Inherited speed {speed:F1}, packages {packageCount} from ACOTester");
    }

    void ReInitializeForReturn()
    {
        if (start == null || end == null)
        {
            Debug.LogWarning("[PathfindingTester] Start or End not assigned for return journey.");
            return;
        }

        // Rebuild waypoints if needed
        if (Waypoints.Count == 0)
        {
            GameObject[] gameObjectsWithWaypointTag = GameObject.FindGameObjectsWithTag("Waypoint");
            foreach (GameObject waypoint in gameObjectsWithWaypointTag)
            {
                if (waypoint.GetComponent<VisGraphWaypointManager>())
                    Waypoints.Add(waypoint);
            }
        }

        // Rebuild connections if needed
        if (AStarManager == null)
            AStarManager = new AStarManager();

        // Clear and rebuild connections
        AStarManager = new AStarManager();
        foreach (GameObject waypoint in Waypoints)
        {
            VisGraphWaypointManager tmpWaypointMan = waypoint.GetComponent<VisGraphWaypointManager>();
            if (tmpWaypointMan == null) continue;
            
            foreach (VisGraphConnection aVisGraphConnection in tmpWaypointMan.Connections)
            {
                if (aVisGraphConnection.ToNode != null)
                {
                    Connection aConnection = new Connection();
                    aConnection.FromNode = waypoint;
                    aConnection.ToNode = aVisGraphConnection.ToNode;
                    AStarManager.AddConnection(aConnection);
                }
            }
        }

        // Path from assigned start to assigned end
        List<Connection> pathToEnd = AStarManager.PathfindAStar(start, end);

        ConnectionArray.Clear();
        ConnectionArray.AddRange(pathToEnd);

        if (ConnectionArray.Count == 0)
        {
            Debug.LogWarning($"[PathfindingTester] No A* path found from {start.name} to {end.name}");
            return;
        }

        startToPickupCount = 0; // No pickup phase
        pickupToEndCount = pathToEnd.Count;
        endToStartCount = 0;

        currentTarget = 0;
        hasPassenger = true; // Already has passengers from ACO
        statusText = "Returning to start using Pathfinding Tester";
        timerRunning = true;
        agentMove = true;

        if (stats != null)
        {
            stats.deliveryStatus = "Returning to start using Pathfinding Tester";
        }

        Debug.Log($"[PathfindingTester] {gameObject.name}: Initialized A* path from {start.name} to {end.name} with {pathToEnd.Count} connections.");
    }

    void Initialize()
    {
        stats = GetComponent<AgentStatsSource>();
        cachedRigidbody = GetComponent<Rigidbody>();
        if (cachedRigidbody == null) cachedRigidbody = GetComponentInChildren<Rigidbody>();

        if (stats != null)
        {
            if (string.IsNullOrWhiteSpace(stats.agentName) || stats.agentName.Trim().Equals("Agent"))
                stats.agentName = transform.root.name;

            stats.deliveryStatus = "Returning to start using Pathfinding Tester";

            // Only reset package count if not using inherited values from ACOTester
            if (resetCustomerCountOnStart && !useInheritedSpeed)
                stats.packageCount = 0;
            else if (useInheritedSpeed)
                stats.packageCount = inheritedPackageCount; // Restore inherited count
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
        statusText = "Returning to start using Pathfinding Tester";
        timerRunning = true;

        isInitialized = true;
    }

    void OnDrawGizmos()
    {
        // Draw collision detection rays in Play mode
        if (Application.isPlaying)
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Gizmos.color = isYielding ? Color.red : Color.green;
            
            // Draw front ray (raycastDistance)
            Gizmos.DrawRay(origin, transform.forward * raycastDistance);
            
            // Draw rear ray (raycastDistance)
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin, -transform.forward * raycastDistance);
            
            // Draw side rays (raycastWidth)
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin, transform.right * raycastWidth);
            Gizmos.DrawRay(origin, -transform.right * raycastWidth);
            
            if (isYielding && detectedAgent != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, detectedAgent.position);
                Gizmos.DrawWireSphere(yieldTargetPosition, 0.5f);
            }
        }
        
        // Draw path
        Gizmos.color = Color.white;
        foreach (Connection aConnection in ConnectionArray)
        {
            if (aConnection.FromNode != null && aConnection.ToNode != null)
            {
                Gizmos.DrawLine(
                    aConnection.FromNode.transform.position + OffSet,
                    aConnection.ToNode.transform.position + OffSet
                );
            }
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
            float moveSpeed;
            
            // If using inherited speed from ACOTester, it already has customer penalty applied
            if (useInheritedSpeed)
            {
                moveSpeed = currentSpeed;
            }
            else
            {
                moveSpeed = currentSpeed;
                int customers = (stats != null) ? Mathf.Max(0, stats.packageCount) : 0;
                float mult = compoundPenalty
                    ? Mathf.Pow(1f - speedPenaltyPerCustomer, customers)
                    : (1f - (speedPenaltyPerCustomer * customers));
                mult = Mathf.Clamp(mult, minCustomerSpeedMultiplier, 1f);
                moveSpeed *= mult;
            }

            currentTargetPos = ConnectionArray[currentTarget].ToNode.transform.position;
            currentTargetPos.y = transform.position.y;

            Vector3 dir = currentTargetPos - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Vector3 forwardDir = dir.normalized;

                // ===== CHECK IF NEARBY AGENT IS PICKING UP =====
                if (!isWaitingForPickup)
                {
                    if (IsNearbyAgentPickingUp())
                    {
                        StartCoroutine(WaitForNearbyPickup());
                        UpdateMeasuredSpeed(prevPos, dt);
                        return;
                    }
                }

                // ===== COLLISION AVOIDANCE =====
                var (shouldStop, speedMult, sideStepDir, isFirstYield) = ProcessCollisionAvoidance();
                
                if (shouldStop || speedMult == 0f)
                {
                    // COMPLETE STOP - yielding to faster agent
                    moveSpeed = 0f;
                    if (cachedRigidbody != null)
                    {
                        cachedRigidbody.velocity = Vector3.zero;
                    }
                    
                    // STEP ASIDE - move full sideStepDistance immediately when first detecting
                    if (isFirstYield)
                    {
                        Vector3 sideStep = transform.right * sideStepDistance;
                        transform.position = transform.position + sideStep;
                        Debug.Log($"[PathfindingTester] {gameObject.name}: Moved aside by {sideStepDistance} units");
                    }
                    
                    if (stats != null) stats.speedMS = 0f;
                    UpdateMeasuredSpeed(prevPos, dt);
                    return;
                }
                
                // Apply speed reduction if yielding but not fully stopped
                moveSpeed *= speedMult;

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
        {
            // If using inherited speed from ACOTester, show the theoretical speed
            // (last ACO speed with customer penalty) instead of measured speed
            if (useInheritedSpeed && agentMove)
            {
                stats.speedMS = GetCurrentSpeed();
            }
            else
            {
                stats.speedMS = currentSpeedValue;
            }
        }
    }

    /// <summary>
    /// Get the current effective speed of this agent (for collision avoidance).
    /// </summary>
    public float GetCurrentSpeed()
    {
        // If using inherited speed from ACOTester, it already has customer penalty applied
        if (useInheritedSpeed)
        {
            return currentSpeed;
        }
        
        // Otherwise, apply customer penalty to base speed
        float moveSpeed = currentSpeed;
        int customers = (stats != null) ? Mathf.Max(0, stats.packageCount) : 0;
        float mult = compoundPenalty
            ? Mathf.Pow(1f - speedPenaltyPerCustomer, customers)
            : (1f - (speedPenaltyPerCustomer * customers));
        mult = Mathf.Clamp(mult, minCustomerSpeedMultiplier, 1f);
        return moveSpeed * mult;
    }

    #region Collision Avoidance - Raycast Based

    /// <summary>
    /// Perform raycast-based collision detection around the agent.
    /// Uses raycastDistance for front/rear and raycastWidth for sides.
    /// Returns info about detected agent if one is nearby.
    /// Also returns whether agent is approaching from behind.
    /// </summary>
    private (bool detected, float otherSpeed, float distance, Transform otherTransform, bool isFromBehind) RaycastForAgentAhead()
    {
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        
        // Calculate max detection range (use larger of the two)
        float maxRange = Mathf.Max(raycastDistance, raycastWidth);
        
        // Use OverlapSphere to find nearby agents
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, maxRange);
        
        foreach (Collider col in nearbyColliders)
        {
            if (col.transform.IsChildOf(transform) || col.transform == transform)
                continue;
            
            // Check ACOTester
            ACOTester otherACO = col.GetComponentInParent<ACOTester>();
            if (otherACO != null && otherACO.enabled && otherACO.IsMoving)
            {
                Vector3 toOther = otherACO.transform.position - transform.position;
                float distance = toOther.magnitude;
                
                // Check if agent is within detection box (front/rear by raycastDistance, sides by raycastWidth)
                if (IsWithinDetectionBox(toOther, forward, right, distance))
                {
                    // Check if the other agent is behind us (approaching from rear)
                    float dotProduct = Vector3.Dot(forward, toOther.normalized);
                    bool isFromBehind = dotProduct < -0.3f; // Agent is behind us
                    
                    // Also check if the other agent is heading towards us
                    Vector3 otherForward = otherACO.transform.forward;
                    float approachDot = Vector3.Dot(otherForward, -toOther.normalized);
                    bool isApproaching = approachDot > 0.3f; // Other agent is heading towards us
                    
                    if (isFromBehind && isApproaching)
                    {
                        return (true, otherACO.CalculateSpeed(), distance, otherACO.transform, true);
                    }
                    else
                    {
                        return (true, otherACO.CalculateSpeed(), distance, otherACO.transform, false);
                    }
                }
            }
            
            // Check PathfindingTester
            PathfindingTester otherPF = col.GetComponentInParent<PathfindingTester>();
            if (otherPF != null && otherPF != this && otherPF.enabled && otherPF.agentMove)
            {
                Vector3 toOther = otherPF.transform.position - transform.position;
                float distance = toOther.magnitude;
                
                if (IsWithinDetectionBox(toOther, forward, right, distance))
                {
                    // Check if the other agent is behind us (approaching from rear)
                    float dotProduct = Vector3.Dot(forward, toOther.normalized);
                    bool isFromBehind = dotProduct < -0.3f; // Agent is behind us
                    
                    // Also check if the other agent is heading towards us
                    Vector3 otherForward = otherPF.transform.forward;
                    float approachDot = Vector3.Dot(otherForward, -toOther.normalized);
                    bool isApproaching = approachDot > 0.3f; // Other agent is heading towards us
                    
                    if (isFromBehind && isApproaching)
                    {
                        return (true, otherPF.GetCurrentSpeed(), distance, otherPF.transform, true);
                    }
                    else
                    {
                        return (true, otherPF.GetCurrentSpeed(), distance, otherPF.transform, false);
                    }
                }
            }
        }

        // Raycast in front and rear (using raycastDistance)
        RaycastHit hit;
        
        // Front raycast
        if (Physics.Raycast(origin, forward, out hit, raycastDistance))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false);
        }
        
        // Rear raycast - agents behind us approaching
        if (Physics.Raycast(origin, -forward, out hit, raycastDistance))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, true);
        }
        
        // Side raycasts (using raycastWidth)
        if (Physics.Raycast(origin, right, out hit, raycastWidth))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false);
        }
        
        if (Physics.Raycast(origin, -right, out hit, raycastWidth))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false);
        }

        return (false, 0f, 0f, null, false);
    }
    
    /// <summary>
    /// Check if a position is within the detection box.
    /// Front/Rear uses raycastDistance, Sides use raycastWidth.
    /// </summary>
    private bool IsWithinDetectionBox(Vector3 toOther, Vector3 forward, Vector3 right, float distance)
    {
        // Project onto forward axis (front/rear)
        float forwardDist = Mathf.Abs(Vector3.Dot(toOther, forward));
        // Project onto right axis (sides)
        float sideDist = Mathf.Abs(Vector3.Dot(toOther, right));
        
        // Check if within detection bounds
        return (forwardDist <= raycastDistance && sideDist <= raycastWidth);
    }
    
    /// <summary>
    /// Check a raycast hit for agent components.
    /// </summary>
    private (bool detected, float otherSpeed, float distance, Transform otherTransform) CheckHitForAgent(RaycastHit hit)
    {
        ACOTester otherACO = hit.collider.GetComponentInParent<ACOTester>();
        if (otherACO != null && otherACO.enabled)
        {
            return (true, otherACO.CalculateSpeed(), hit.distance, otherACO.transform);
        }

        PathfindingTester otherPF = hit.collider.GetComponentInParent<PathfindingTester>();
        if (otherPF != null && otherPF != this && otherPF.enabled)
        {
            return (true, otherPF.GetCurrentSpeed(), hit.distance, otherPF.transform);
        }
        
        return (false, 0f, 0f, null);
    }

    /// <summary>
    /// Determine if this agent should yield based on speed comparison.
    /// </summary>
    private bool ShouldYieldTo(float otherSpeed)
    {
        float mySpeed = GetCurrentSpeed();
        return otherSpeed > mySpeed * 1.05f;
    }

    /// <summary>
    /// Calculate the side-step position to avoid collision.
    /// Moves perpendicular to our forward direction (to the right).
    /// </summary>
    private Vector3 CalculateSideStepPosition(Transform otherAgent)
    {
        // Move to the right relative to our forward direction
        Vector3 rightDir = transform.right;
        Vector3 sideStepPos = transform.position + rightDir * sideStepDistance;
        sideStepPos.y = transform.position.y;
        return sideStepPos;
    }

    /// <summary>
    /// Check if the detected agent is still in our path.
    /// </summary>
    private bool IsAgentStillBlocking()
    {
        if (detectedAgent == null) return false;
        
        Vector3 toAgent = detectedAgent.position - transform.position;
        float distance = toAgent.magnitude;
        
        if (distance > raycastDistance * 1.5f) return false;
        
        float dotProduct = Vector3.Dot(transform.forward, toAgent.normalized);
        if (dotProduct < 0.1f) return false;
        
        return distance < safeDistance * 2f;
    }

    /// <summary>
    /// Execute collision avoidance: slow down, move aside, wait for 3 seconds.
    /// Returns: (shouldStop, speedMultiplier, sideStepDir, isFirstYield)
    /// </summary>
    private (bool shouldStop, float speedMultiplier, Vector3 sideStepDir, bool isFirstYield) ProcessCollisionAvoidance()
    {
        float dt = Time.deltaTime;
        float mySpeed = GetCurrentSpeed();

        if (isYielding)
        {
            yieldTimer += dt;
            
            // Wait for exactly yieldWaitTime (3 seconds by default) before resuming
            if (yieldTimer >= yieldWaitTime)
            {
                isYielding = false;
                detectedAgent = null;
                yieldTimer = 0f;
                statusText = "Resuming path";
                if (stats != null) stats.deliveryStatus = "A*: Resuming";
                Debug.Log($"[PathfindingTester] {gameObject.name}: Wait complete ({yieldWaitTime}s), resuming path");
                return (false, 1f, Vector3.zero, false);
            }

            // Still waiting - complete stop, no movement
            statusText = $"Waiting... ({yieldWaitTime - yieldTimer:F1}s)";
            if (stats != null) stats.deliveryStatus = statusText;
            return (true, 0f, Vector3.zero, false);
        }

        var (detected, otherSpeed, distance, otherTransform, isFromBehind) = RaycastForAgentAhead();
        
        float maxDetectRange = Mathf.Max(raycastDistance, raycastWidth);
        if (detected && distance < maxDetectRange)
        {
            // If faster agent is approaching from behind, step aside immediately
            if (isFromBehind && ShouldYieldTo(otherSpeed))
            {
                isYielding = true;
                detectedAgent = otherTransform;
                yieldTimer = 0f;
                yieldTargetPosition = CalculateSideStepPosition(otherTransform);
                
                // Get other agent name for HUD message
                string otherName = otherTransform.root.name;
                string myName = stats != null ? stats.agentName : gameObject.name;
                AgentStatsSource.lastCollisionMessage = $"{myName} yielded to {otherName} (rear)";
                
                statusText = $"STEPPED ASIDE (rear approach) - Waiting {yieldWaitTime}s";
                if (stats != null) stats.deliveryStatus = statusText;
                Debug.Log($"[PathfindingTester] {gameObject.name}: FASTER AGENT APPROACHING FROM BEHIND! Stepping aside by {sideStepDistance} units and waiting {yieldWaitTime}s (my speed: {mySpeed:F1}, other: {otherSpeed:F1})");

                // STEP RIGHT - perpendicular to forward direction (first yield frame)
                Vector3 sideDir = transform.right;
                return (true, 0f, sideDir, true);
            }
            // Normal front collision - slower agent yields
            else if (ShouldYieldTo(otherSpeed))
            {
                isYielding = true;
                detectedAgent = otherTransform;
                yieldTimer = 0f;
                yieldTargetPosition = CalculateSideStepPosition(otherTransform);
                
                // Get other agent name for HUD message
                string otherName = otherTransform.root.name;
                string myName = stats != null ? stats.agentName : gameObject.name;
                AgentStatsSource.lastCollisionMessage = $"{myName} yielded to {otherName}";
                
                statusText = $"STEPPED ASIDE - Waiting {yieldWaitTime}s";
                if (stats != null) stats.deliveryStatus = statusText;
                Debug.Log($"[PathfindingTester] {gameObject.name}: STEPPING ASIDE by {sideStepDistance} units and waiting {yieldWaitTime}s for faster agent (my speed: {mySpeed:F1}, other: {otherSpeed:F1})");

                // STEP RIGHT - perpendicular to forward direction (first yield frame)
                Vector3 sideDir = transform.right;
                return (true, 0f, sideDir, true);
            }
            else
            {
                if (distance < safeDistance)
                {
                    return (false, 0.5f, Vector3.zero, false);
                }
            }
        }

        return (false, 1f, Vector3.zero, false);
    }

    #endregion

    /// <summary>
    /// Check if a nearby agent is picking up a customer.
    /// </summary>
    private bool IsNearbyAgentPickingUp()
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, 7f);

        foreach (var col in nearbyColliders)
        {
            if (col.transform.IsChildOf(transform) || col.transform == transform)
                continue;

            // Check for ACOTester picking up
            ACOTester acoAgent = col.GetComponentInParent<ACOTester>();
            if (acoAgent != null && acoAgent.IsPickingUp)
                return true;

            // Check for PathfindingTester picking up
            PathfindingTester pfAgent = col.GetComponentInParent<PathfindingTester>();
            if (pfAgent != null && pfAgent != this && pfAgent.IsPickingUp)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Wait for 3 seconds when a nearby agent is picking up a customer.
    /// </summary>
    private IEnumerator WaitForNearbyPickup()
    {
        isWaitingForPickup = true;
        agentMove = false;

        statusText = "Waiting for pickup";
        if (stats != null)
            stats.deliveryStatus = "Waiting for pickup";

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        // Wait for 3 seconds
        yield return new WaitForSeconds(3f);

        isWaitingForPickup = false;
        agentMove = true;

        statusText = "Resuming path";
        if (stats != null)
            stats.deliveryStatus = "Returning";
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
        isPickingUp = true; // Mark as picking up for other agents to detect
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

        isPickingUp = false; // Done picking up
        agentMove = true;
    }
}
