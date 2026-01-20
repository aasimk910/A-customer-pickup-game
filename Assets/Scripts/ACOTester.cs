using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Agent Tester for Part 3.
/// - Uses ACO to navigate between multiple goal locations
/// - Picks up or drops parcels at each goal (3 second hold)
/// - Speed changes by 10% per parcel (max 90% change)
/// - Switches to A* (PathfindingTester) after all parcels handled
/// - Returns to start using A* and stops completely
/// </summary>
public class ACOTester : MonoBehaviour
{
    #region Enums and Configuration
    
    [Header("Scenario Type")]
    [Tooltip("Pickup: Speed decreases (0.9^parcels). Drop: Speed increases (1.1^parcels).")]
    public ScenarioType scenarioType = ScenarioType.Pickup;

    public enum ScenarioType
    {
        Pickup,
        Drop
    }
    
    #endregion

    #region Inspector Fields
    
    [Header("Agent Configuration")]
    [Tooltip("The starting waypoint for this agent (must be unique per agent)")]
    public GameObject startNode;

    [Tooltip("Color of the car (used for gizmo lines)")]
    public Color carColor = Color.cyan;

    [Tooltip("Goal waypoints where customers will be picked up (assign via Inspector)")]
    public List<GameObject> goalNodes = new List<GameObject>();

    [Tooltip("Customers to pick up at each goal node (must match goalNodes count)")]
    public List<GameObject> customers = new List<GameObject>();

    [Tooltip("Passenger seat where customers will sit after pickup")]
    public Transform passengerSeat;

    [Tooltip("Duration to move customer to seat")]
    public float seatMoveDuration = 1.0f;

    [Header("Pickup Sound")]
    [Tooltip("Sound to play when customer is picked up")]
    public AudioClip pickupSound;
    [Tooltip("Audio source to play the sound (optional)")]
    public AudioSource audioSource;

    [Header("Speed Settings")]
    [Tooltip("Base speed of the agent")]
    public float baseSpeed = 8f;

    [Tooltip("Current speed (read-only, calculated based on parcels)")]
    [SerializeField] private float currentSpeed = 0f;

    [Tooltip("Turn/rotation speed")]
    public float turnSpeed = 5f;

    [Header("Movement Settings")]
    [Tooltip("Distance to consider waypoint reached")]
    public float waypointReachDistance = 1.5f;

    [Tooltip("Hold time at each goal (pickup/drop parcel)")]
    public float holdTimeAtGoal = 3f;

    [Header("References")]
    [Tooltip("ACO Manager reference")]
    public ACOManager acoManager;

    [Tooltip("A* Pathfinding Tester (for return journey - should be disabled initially)")]
    public PathfindingTester aStarTester;
    
    #endregion

    #region Runtime Status Fields
    
    [Header("Runtime Status (Read Only)")]
    [SerializeField] private int currentParcelCount = 0;
    [SerializeField] private int parcelsHandled = 0;
    [SerializeField] private string currentStatus = "Initializing";
    [SerializeField] private float totalDistanceTravelled = 0f;
    [SerializeField] private bool isMoving = false;
    [SerializeField] private int currentGoalIndex = 0;
    
    #endregion

    #region Private State Variables
    
    // Track picked up customers
    private List<GameObject> pickedUpCustomers = new List<GameObject>();

    // Mapping from goal to customer (for ACO selection)
    private Dictionary<GameObject, GameObject> goalToCustomerMap = new Dictionary<GameObject, GameObject>();

    // Public properties for UI
    public int CurrentParcelCount => currentParcelCount;
    public int ParcelsHandled => parcelsHandled;
    public float CurrentSpeed => currentSpeed;
    public string CurrentStatus => currentStatus;
    public float TotalDistanceTravelled => totalDistanceTravelled;
    public bool IsMoving => isMoving;
    public ScenarioType Scenario => scenarioType;

    // Internal state
    private List<ACOConnection> currentPath = new List<ACOConnection>();
    private List<ACOConnection> acoRoute = new List<ACOConnection>();
    private Vector3 lastPosition;
    private Vector3 offsetY = new Vector3(0, 0.3f, 0);

    private bool isHolding = false;
    public bool IsPickingUp => isHolding; // Public property for other agents to check
    private bool isWaitingForPickup = false; // Waiting for another agent picking up
    private bool allParcelsHandled = false;
    private bool returnedToStart = false;
    private GameObject lastVisitedNode = null; // Track the last waypoint visited for handoff to PathfindingTester
    
    #endregion

    #region Collision Avoidance State
    
    // Collision Avoidance State - Raycast-based
    private bool isYielding = false;
    private Vector3 yieldTargetPosition;
    private float yieldTimer = 0f;
    private Transform detectedAgent = null;
    
    [Header("Collision Avoidance Settings")]
    [SerializeField] private float raycastDistance = 20f;        // How far to detect in front and rear
    [SerializeField] private float raycastWidth = 8f;            // How far to detect on sides (left/right)
    [SerializeField] private float sideStepDistance = 5f;        // How far to move aside immediately
    [SerializeField] private float sideStepBackDistance = 10f;   // How far to move back on side collision
    [SerializeField] private float yieldWaitTime = 3f;           // How long to wait after stepping aside
    [SerializeField] private float safeDistance = 8f;            // Min distance to maintain
    
    public bool IsYielding => isYielding;              // Public for HUD/debugging
    
    #endregion

    #region Component References and Cached Values
    
    // Component references
    private AgentStatsSource stats;
    private Rigidbody cachedRigidbody;
    
    // Cached values for performance
    private static Collider[] overlapResults = new Collider[20];  // Reusable array for Physics.OverlapSphereNonAlloc
    private Vector3 cachedForward;
    private Vector3 cachedRight;
    private Vector3 cachedPosition;
    
    #endregion

    #region Unity Lifecycle Methods

    void Start()
    {
        stats = GetComponent<AgentStatsSource>();
        cachedRigidbody = GetComponent<Rigidbody>();
        lastVisitedNode = startNode; // Initialize to start node
        if (cachedRigidbody == null)
            cachedRigidbody = GetComponentInChildren<Rigidbody>();

        // Initialize parcel count based on scenario
        if (scenarioType == ScenarioType.Pickup)
        {
            currentParcelCount = 0; // Start with 0, pick up customers
        }
        else
        {
            currentParcelCount = customers.Count; // Start with customers, drop them
        }

        // Update stats
        if (stats != null)
        {
            stats.packageCount = currentParcelCount;
            stats.deliveryStatus = "Initializing ACO";
        }

        // Validate setup
        if (!ValidateSetup())
            return;

        // Find ACO Manager if not assigned
        if (acoManager == null)
            acoManager = FindObjectOfType<ACOManager>();

        if (acoManager == null)
        {
            Debug.LogError($"[ACOTester] {gameObject.name}: ACOManager not found!");
            enabled = false;
            return;
        }

        // Build goal-to-customer mapping
        goalToCustomerMap.Clear();
        for (int i = 0; i < goalNodes.Count; i++)
        {
            if (goalNodes[i] != null && i < customers.Count && customers[i] != null)
            {
                goalToCustomerMap[goalNodes[i]] = customers[i];
            }
        }

        // Position agent at start
        transform.position = startNode.transform.position + offsetY;
        lastPosition = transform.position;

        // Start ACO navigation
        StartCoroutine(ACONavigationRoutine());
    }

    bool ValidateSetup()
    {
        if (startNode == null)
        {
            Debug.LogError($"[ACOTester] {gameObject.name}: No start waypoint assigned!");
            enabled = false;
            return false;
        }

        if (startNode.GetComponent<VisGraphWaypointManager>() == null)
        {
            Debug.LogError($"[ACOTester] {gameObject.name}: Start node is not a waypoint!");
            enabled = false;
            return false;
        }

        if (goalNodes.Count == 0)
        {
            Debug.LogError($"[ACOTester] {gameObject.name}: No goal waypoints assigned!");
            enabled = false;
            return false;
        }

        // Validate goal nodes
        foreach (var goal in goalNodes)
        {
            if (goal == null)
            {
                Debug.LogWarning($"[ACOTester] {gameObject.name}: Null goal node in list!");
            }
        }

        return true;
    }

    void Update()
    {
        if (!enabled || returnedToStart)
            return;

        // Track distance
        float frameDist = Vector3.Distance(transform.position, lastPosition);
        totalDistanceTravelled += frameDist;
        lastPosition = transform.position;

        // Update stats
        if (stats != null)
        {
            stats.totalDistanceM = totalDistanceTravelled;
            stats.speedMS = currentSpeed;
            stats.packageCount = currentParcelCount;
            stats.deliveryStatus = currentStatus;
        }
    }
    
    #endregion

    #region Speed Calculation

    /// <summary>
    /// Calculate speed based on parcel count.
    /// Pickup: baseSpeed * 0.9^parcelCount (speed decreases)
    /// Drop: baseSpeed * 1.1^parcelsDelivered (speed increases as parcels dropped)
    /// Max change is 90% (multiplier clamped between 0.1 and 1.9)
    /// </summary>
    public float CalculateSpeed()
    {
        float multiplier;

        if (scenarioType == ScenarioType.Pickup)
        {
            // Speed decreases with more parcels picked up
            multiplier = Mathf.Pow(0.9f, currentParcelCount);
        }
        else
        {
            // Speed increases as parcels are dropped (fewer parcels = faster)
            multiplier = Mathf.Pow(1.1f, parcelsHandled);
        }

        // Clamp to max 90% change
        multiplier = Mathf.Clamp(multiplier, 0.1f, 1.9f);

        return baseSpeed * multiplier;
    }
    
    #endregion

    #region ACO Navigation

    /// <summary>
    /// Main ACO navigation coroutine.
    /// </summary>
    IEnumerator ACONavigationRoutine()
    {
        yield return new WaitForSeconds(0.5f); // Wait for ACOManager initialization

        currentStatus = "Running ACO algorithm...";
        isMoving = true;

        // Run ACO to get optimal route through all goals
        acoRoute = acoManager.RunACO(startNode);

        if (acoRoute.Count == 0)
        {
            Debug.LogWarning($"[ACOTester] {gameObject.name}: ACO returned no route!");
        }

        currentStatus = "ACO: Navigating to customers";

        // Track remaining goals to pick up
        List<GameObject> remainingGoals = new List<GameObject>(goalNodes);
        
        int totalCustomers = goalNodes.Count;

        // Navigate to goals using ACO selection (random based on pheromones)
        while (remainingGoals.Count > 0 && parcelsHandled < totalCustomers)
        {
            // Select next goal using ACO probability
            int selectedIndex = SelectNextGoalACO(remainingGoals);
            
            if (selectedIndex < 0 || selectedIndex >= remainingGoals.Count)
            {
                Debug.LogWarning($"[ACOTester] Invalid goal index selected: {selectedIndex}");
                break;
            }

            GameObject currentGoal = remainingGoals[selectedIndex];
            
            // Get the customer mapped to this goal (using the dictionary)
            GameObject currentCustomer = null;
            if (goalToCustomerMap.ContainsKey(currentGoal))
            {
                currentCustomer = goalToCustomerMap[currentGoal];
            }

            if (currentGoal == null)
            {
                remainingGoals.RemoveAt(selectedIndex);
                continue;
            }

            // Get customer name for status
            string customerName = currentCustomer != null ? currentCustomer.name : "customer";

            // Find path to current goal using ACO pheromones
            GameObject nearestWaypoint = acoManager.FindNearestWaypoint(transform.position);
            if (nearestWaypoint == null)
                nearestWaypoint = startNode;

            currentPath = acoManager.FindPathToGoal(nearestWaypoint, currentGoal);

            if (currentPath.Count == 0)
            {
                // Direct movement if no path found
                currentStatus = $"ACO: Going to {customerName}";
                yield return StartCoroutine(MoveDirectlyTo(currentGoal));
            }
            else
            {
                // Follow ACO path
                currentStatus = $"ACO: Going to {customerName}";
                yield return StartCoroutine(FollowPath(currentPath));
            }

            // Move to exact goal position
            yield return StartCoroutine(MoveDirectlyTo(currentGoal));

            // Store current customer for pickup (using the selected one, not by index)
            currentGoalIndex = goalNodes.IndexOf(currentGoal);
            
            // Temporarily set the customer to pick up
            if (currentCustomer != null)
            {
                int originalIndex = customers.IndexOf(currentCustomer);
                if (originalIndex >= 0)
                    currentGoalIndex = originalIndex;
            }

            // Handle customer pickup at goal (switches to A* immediately after last customer)
            yield return StartCoroutine(HandleParcelAtGoalWithCustomer(currentGoal, currentCustomer));
            
            // Remove picked up goal from remaining list
            remainingGoals.RemoveAt(selectedIndex);
            
            // Check if we already switched to A* (last customer was picked up)
            if (allParcelsHandled)
                yield break;
        }
        
        // Fallback: if loop completes without switching (shouldn't happen normally)
        if (!allParcelsHandled)
        {
            allParcelsHandled = true;
            currentSpeed = 0f;
            isMoving = false;
            currentStatus = "All customers picked up. Switching to A*...";
            yield return StartCoroutine(SwitchToAStar());
        }
    }
    
    #endregion

    #region ACO Goal Selection

    /// <summary>
    /// Select the next goal using ACO probability (pheromone levels + distance heuristic).
    /// </summary>
    int SelectNextGoalACO(List<GameObject> remainingGoals)
    {
        if (remainingGoals.Count == 0)
            return -1;
        
        if (remainingGoals.Count == 1)
            return 0;

        // Calculate probabilities for each remaining goal
        float[] probabilities = new float[remainingGoals.Count];
        float totalProbability = 0f;

        Vector3 currentPos = transform.position;

        for (int i = 0; i < remainingGoals.Count; i++)
        {
            GameObject goal = remainingGoals[i];
            if (goal == null)
            {
                probabilities[i] = 0f;
                continue;
            }

            // Get distance to goal (heuristic)
            float distance = Vector3.Distance(currentPos, goal.transform.position);
            if (distance < 0.1f) distance = 0.1f; // Avoid division by zero
            
            // Get pheromone level on path to this goal
            float pheromone = GetPheromoneToGoal(goal);
            
            // ACO probability: (pheromone^alpha) * (1/distance)^beta
            float alpha = acoManager.Alpha;
            float beta = acoManager.Beta;
            
            float probability = Mathf.Pow(pheromone, alpha) * Mathf.Pow(1f / distance, beta);
            probabilities[i] = probability;
            totalProbability += probability;
        }

        // Normalize and select using roulette wheel
        if (totalProbability <= 0f)
        {
            // Fallback to random selection
            return Random.Range(0, remainingGoals.Count);
        }

        float randomValue = Random.Range(0f, totalProbability);
        float cumulative = 0f;

        for (int i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (randomValue <= cumulative)
                return i;
        }

        return remainingGoals.Count - 1; // Fallback
    }

    /// <summary>
    /// Get average pheromone level on path to a goal.
    /// </summary>
    float GetPheromoneToGoal(GameObject goal)
    {
        GameObject nearestWaypoint = acoManager.FindNearestWaypoint(transform.position);
        if (nearestWaypoint == null)
            return acoManager.DefaultPheromone;

        List<ACOConnection> pathToGoal = acoManager.FindPathToGoal(nearestWaypoint, goal);
        
        if (pathToGoal.Count == 0)
            return acoManager.DefaultPheromone;

        float totalPheromone = 0f;
        foreach (var conn in pathToGoal)
        {
            totalPheromone += conn.PheromoneLevel;
        }

        return totalPheromone / pathToGoal.Count;
    }
    
    #endregion

    #region Path Following and Movement

    /// <summary>
    /// Follow a path of ACO connections.
    /// </summary>
    IEnumerator FollowPath(List<ACOConnection> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            GameObject targetNode = path[i].ToNode;
            yield return StartCoroutine(MoveToWaypoint(targetNode));
        }
    }

    /// <summary>
    /// Move to a specific waypoint.
    /// </summary>
    IEnumerator MoveToWaypoint(GameObject targetNode)
    {
        Vector3 targetPos = targetNode.transform.position;
        targetPos.y = transform.position.y;
        
        // Update last visited node when we start moving to a new waypoint
        lastVisitedNode = targetNode;

        while (Vector3.Distance(transform.position, targetPos) > waypointReachDistance)
        {
            if (isHolding || !enabled)
            {
                currentSpeed = 0f;
                yield return null;
                continue;
            }

            // Check if another agent nearby is picking up a customer
            if (!isWaitingForPickup)
            {
                if (IsNearbyAgentPickingUp())
                {
                    yield return StartCoroutine(WaitForPickup());
                }
            }

            float dt = Time.deltaTime;
            currentSpeed = CalculateSpeed();

            // ===== COLLISION AVOIDANCE =====
            var (shouldStop, speedMult, sideStepDir, isFirstYield) = ProcessCollisionAvoidance();
            
            if (shouldStop || speedMult == 0f)
            {
                // COMPLETE STOP - yielding to faster agent
                currentSpeed = 0f;
                if (cachedRigidbody != null)
                {
                    cachedRigidbody.velocity = Vector3.zero;
                }
                
                // STEP ASIDE - move full sideStepDistance immediately when first detecting
                if (isFirstYield)
                {
                    Vector3 sideStep = transform.right * sideStepDistance;
                    transform.position = transform.position + sideStep;
                    Debug.Log($"[ACOTester] {gameObject.name}: Moved aside by {sideStepDistance} units");
                }
                
                if (stats != null) stats.speedMS = 0f;
                yield return null;
                continue;
            }
            
            // Apply speed reduction if yielding but not fully stopped
            currentSpeed *= speedMult;

            // Normal movement towards target
            Vector3 direction = (targetPos - transform.position).normalized;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);
                transform.position += direction * currentSpeed * dt;
            }
            
            if (stats != null) stats.speedMS = currentSpeed;

            yield return null;
        }
    }

    /// <summary>
    /// Move directly to a position (for final approach to goal).
    /// </summary>
    IEnumerator MoveDirectlyTo(GameObject target)
    {
        Vector3 targetPos = target.transform.position;
        targetPos.y = transform.position.y;

        while (Vector3.Distance(transform.position, targetPos) > waypointReachDistance * 0.5f)
        {
            if (isHolding)
            {
                currentSpeed = 0f;
                yield return null;
                continue;
            }

            float dt = Time.deltaTime;
            currentSpeed = CalculateSpeed();

            // ===== COLLISION AVOIDANCE =====
            var (shouldStop, speedMult, sideStepDir, isFirstYield) = ProcessCollisionAvoidance();
            
            if (shouldStop || speedMult == 0f)
            {
                // COMPLETE STOP - yielding to faster agent
                currentSpeed = 0f;
                if (cachedRigidbody != null)
                {
                    cachedRigidbody.velocity = Vector3.zero;
                }
                
                // STEP ASIDE - move full sideStepDistance immediately when first detecting
                if (isFirstYield)
                {
                    Vector3 sideStep = transform.right * sideStepDistance;
                    transform.position = transform.position + sideStep;
                    Debug.Log($"[ACOTester] {gameObject.name}: Moved aside by {sideStepDistance} units");
                }
                
                if (stats != null) stats.speedMS = 0f;
                yield return null;
                continue;
            }
            
            // Apply speed reduction if yielding but not fully stopped
            currentSpeed *= speedMult;

            Vector3 direction = (targetPos - transform.position).normalized;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);
                transform.position += direction * currentSpeed * dt;
            }
            
            if (stats != null) stats.speedMS = currentSpeed;

            yield return null;
        }
    }
    
    #endregion

    #region Customer Pickup and Handling

    /// <summary>
    /// Handle customer pickup at goal with specific customer (for ACO random selection).
    /// </summary>
    IEnumerator HandleParcelAtGoalWithCustomer(GameObject goal, GameObject customerToPickup)
    {
        isHolding = true;
        currentSpeed = 0f; // Speed must be exactly 0
        isMoving = false;

        // Stop rigidbody
        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        string customerName = customerToPickup != null ? customerToPickup.name : "customer";

        if (scenarioType == ScenarioType.Pickup)
        {
            currentStatus = $"Picking up {customerName}...";
        }
        else
        {
            currentStatus = $"Dropping {customerName}...";
        }

        // Hold for 3 seconds
        yield return new WaitForSeconds(holdTimeAtGoal);

        // Handle the customer
        if (scenarioType == ScenarioType.Pickup && customerToPickup != null)
        {
            // Play pickup sound for 1 second
            if (pickupSound != null)
            {
                StartCoroutine(PlaySoundForDuration(1f));
            }

            // Move customer to passenger seat
            yield return StartCoroutine(MoveCustomerToSeat(customerToPickup));
            pickedUpCustomers.Add(customerToPickup);
            currentParcelCount++;
        }
        else if (scenarioType == ScenarioType.Drop)
        {
            currentParcelCount = Mathf.Max(0, currentParcelCount - 1);
        }

        parcelsHandled++;

        if (stats != null)
            stats.packageCount = currentParcelCount;

        isHolding = false;
        
        // Check if this was the last customer - switch to A* immediately
        if (parcelsHandled >= customers.Count)
        {
            allParcelsHandled = true;
            currentSpeed = 0f;
            isMoving = false;
            currentStatus = "All customers picked up! Switching to A*...";
            
            // Switch to A* immediately after last pickup
            yield return StartCoroutine(SwitchToAStar());
            yield break; // Exit this coroutine
        }
        
        isMoving = true;
        currentStatus = $"Picked up {parcelsHandled}/{customers.Count} customers";
    }

    /// <summary>
    /// Play pickup sound for a specified duration.
    /// </summary>
    IEnumerator PlaySoundForDuration(float duration)
    {
        if (audioSource != null)
        {
            audioSource.clip = pickupSound;
            audioSource.Play();
            yield return new WaitForSeconds(duration);
            audioSource.Stop();
        }
        else
        {
            // Create temporary audio source for duration control
            GameObject tempAudio = new GameObject("TempAudio");
            tempAudio.transform.position = transform.position;
            AudioSource tempSource = tempAudio.AddComponent<AudioSource>();
            tempSource.clip = pickupSound;
            tempSource.Play();
            yield return new WaitForSeconds(duration);
            tempSource.Stop();
            Destroy(tempAudio);
        }
    }

    /// <summary>
    /// Move customer to the passenger seat.
    /// </summary>
    IEnumerator MoveCustomerToSeat(GameObject customer)
    {
        if (customer == null)
            yield break;

        // If no passenger seat, just hide the customer
        if (passengerSeat == null)
        {
            customer.SetActive(false);
            yield break;
        }

        // Disable any AI/movement on customer
        var customerAnimator = customer.GetComponent<Animator>();
        if (customerAnimator != null)
            customerAnimator.enabled = false;

        var customerNavAgent = customer.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (customerNavAgent != null)
            customerNavAgent.enabled = false;

        // Smoothly move customer to seat
        Vector3 startPos = customer.transform.position;
        Quaternion startRot = customer.transform.rotation;
        float elapsed = 0f;

        while (elapsed < seatMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seatMoveDuration);
            t = t * t * (3f - 2f * t); // Smoothstep

            customer.transform.position = Vector3.Lerp(startPos, passengerSeat.position, t);
            customer.transform.rotation = Quaternion.Slerp(startRot, passengerSeat.rotation, t);

            yield return null;
        }

        // Parent customer to the car so they move with it
        customer.transform.SetParent(passengerSeat);
        customer.transform.localPosition = Vector3.zero;
        customer.transform.localRotation = Quaternion.identity;
    }
    
    #endregion

    #region A* Pathfinding Transition

    /// <summary>
    /// Switch from ACO to A* for return journey.
    /// Configures PathfindingTester to return to the start point.
    /// </summary>
    IEnumerator SwitchToAStar()
    {
        // Set speed to exactly 0
        currentSpeed = 0f;
        isMoving = false;

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        currentStatus = "ACO complete. Switching to A*...";

        yield return new WaitForSeconds(1f);

        // Try to find PathfindingTester if not assigned
        if (aStarTester == null)
        {
            aStarTester = GetComponent<PathfindingTester>();
            if (aStarTester == null)
            {
                aStarTester = GetComponentInChildren<PathfindingTester>();
            }
            if (aStarTester == null)
            {
                aStarTester = GetComponentInParent<PathfindingTester>();
            }
        }

        // Enable A* Tester for return journey
        if (aStarTester != null)
        {
            Debug.Log($"[ACOTester] {gameObject.name}: Found PathfindingTester, enabling for return journey...");

            // Pass the last visited node as start and ACO's start node as end
            // This makes PathfindingTester navigate from where ACO ended back to where it started
            GameObject pathfindingStart = lastVisitedNode ?? acoManager.FindNearestWaypoint(transform.position);
            GameObject pathfindingEnd = startNode;

            // Enable PathfindingTester and initialize it for return journey with proper nodes
            aStarTester.enabled = true;
            aStarTester.InitializeForReturn(pathfindingStart, pathfindingEnd); // Pass start and end nodes
            
            // Pass the current speed and package count to PathfindingTester
            float lastSpeed = CalculateSpeed();
            aStarTester.SetInheritedSpeed(lastSpeed, currentParcelCount);

            if (stats != null)
                stats.deliveryStatus = "A*: Returning to start";

            Debug.Log($"[ACOTester] {gameObject.name}: Switched to A* for return journey.");
            
            // Disable this ACO Tester - this will stop all coroutines automatically
            this.enabled = false;
            
            // Coroutine will stop when component is disabled
            yield break;
        }
        else
        {
            // Simple return if no A* tester available
            Debug.LogWarning($"[ACOTester] {gameObject.name}: No PathfindingTester found! Using simple return.");
            yield return StartCoroutine(SimpleReturnToStart());
        }
    }

    /// <summary>
    /// Simple return to start if A* tester not available.
    /// </summary>
    IEnumerator SimpleReturnToStart()
    {
        currentStatus = "Returning to start...";
        isMoving = true;

        Vector3 startPos = startNode.transform.position;
        startPos.y = transform.position.y;

        while (Vector3.Distance(transform.position, startPos) > waypointReachDistance)
        {
            currentSpeed = CalculateSpeed();

            Vector3 direction = (startPos - transform.position).normalized;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                transform.position += direction * currentSpeed * Time.deltaTime;
            }

            yield return null;
        }

        // Stop completely
        currentSpeed = 0f;
        isMoving = false;
        returnedToStart = true;
        currentStatus = "Stopped";

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        if (stats != null)
            stats.deliveryStatus = "Stopped";

        this.enabled = false;
    }
    
    #endregion

    #region Nearby Agent Detection

    /// <summary>
    /// Find a nearby agent that is currently picking up a customer.
    /// Checks both ACOTester and PathfindingTester agents.
    /// Uses NonAlloc version to avoid allocations.
    /// </summary>
    bool IsNearbyAgentPickingUp()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, 7f, overlapResults);

        for (int i = 0; i < numColliders; i++)
        {
            var col = overlapResults[i];
            if (col.transform.IsChildOf(transform) || col.transform == transform)
                continue;

            // Check ACOTester
            ACOTester acoAgent = col.GetComponentInParent<ACOTester>();
            if (acoAgent != null && acoAgent != this && acoAgent.IsPickingUp)
            {
                return true;
            }

            // Check PathfindingTester
            PathfindingTester pfAgent = col.GetComponentInParent<PathfindingTester>();
            if (pfAgent != null && pfAgent.IsPickingUp)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wait for another agent to finish picking up a customer (3 seconds), then continue.
    /// </summary>
    IEnumerator WaitForPickup()
    {
        isWaitingForPickup = true;
        currentSpeed = 0f;
        isMoving = false;

        currentStatus = "Waiting for pickup";

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        // Wait for 3 seconds
        yield return new WaitForSeconds(3f);

        isWaitingForPickup = false;
        isMoving = true;
        currentStatus = "Resuming path";
    }
    
    #endregion

    #region Collision Avoidance - Raycast Based

    /// <summary>
    /// Perform raycast-based collision detection around the agent.
    /// Uses raycastDistance for front/rear and raycastWidth for sides.
    /// Returns info about detected agent if one is nearby.
    /// Also returns whether agent is approaching from behind and if detection is from side.
    /// Uses NonAlloc version to avoid GC allocations.
    /// </summary>
    private (bool detected, float otherSpeed, float distance, Transform otherTransform, bool isFromBehind, bool isFromSide) RaycastForAgentAhead()
    {
        cachedPosition = transform.position;
        cachedForward = transform.forward;
        cachedRight = transform.right;
        Vector3 origin = cachedPosition + Vector3.up;
        
        // Calculate max detection range (use larger of the two)
        float maxRange = Mathf.Max(raycastDistance, raycastWidth);
        
        // Use OverlapSphereNonAlloc to find nearby agents without allocation
        int numColliders = Physics.OverlapSphereNonAlloc(cachedPosition, maxRange, overlapResults);
        
        for (int i = 0; i < numColliders; i++)
        {
            Collider col = overlapResults[i];
            if (col.transform.IsChildOf(transform) || col.transform == transform)
                continue;
            
            // Check ACOTester
            ACOTester otherACO = col.GetComponentInParent<ACOTester>();
            if (otherACO != null && otherACO != this && otherACO.enabled && otherACO.isMoving)
            {
                Vector3 toOther = otherACO.transform.position - cachedPosition;
                float distance = toOther.magnitude;
                
                // Check if agent is within detection box (front/rear by raycastDistance, sides by raycastWidth)
                if (IsWithinDetectionBox(toOther, cachedForward, cachedRight, distance))
                {
                    // Check if the other agent is behind us (approaching from rear)
                    float dotProduct = Vector3.Dot(cachedForward, toOther.normalized);
                    bool isFromBehind = dotProduct < -0.3f; // Agent is behind us
                    
                    // Also check if the other agent is heading towards us
                    Vector3 otherForward = otherACO.transform.forward;
                    float approachDot = Vector3.Dot(otherForward, -toOther.normalized);
                    bool isApproaching = approachDot > 0.3f; // Other agent is heading towards us
                    
                    if (isFromBehind && isApproaching)
                    {
                        return (true, otherACO.CalculateSpeed(), distance, otherACO.transform, true, false);
                    }
                    else
                    {
                        // Check if detection is from the side
                        float forwardDot = Vector3.Dot(cachedForward, toOther.normalized);
                        bool isSideDetection = Mathf.Abs(forwardDot) < 0.5f; // Side if not mostly in front or behind
                        return (true, otherACO.CalculateSpeed(), distance, otherACO.transform, false, isSideDetection);
                    }
                }
            }
            
            // Check PathfindingTester
            PathfindingTester otherPF = col.GetComponentInParent<PathfindingTester>();
            if (otherPF != null && otherPF.enabled)
            {
                Vector3 toOther = otherPF.transform.position - cachedPosition;
                float distance = toOther.magnitude;
                
                if (IsWithinDetectionBox(toOther, cachedForward, cachedRight, distance))
                {
                    // Check if the other agent is behind us (approaching from rear)
                    float dotProduct = Vector3.Dot(cachedForward, toOther.normalized);
                    bool isFromBehind = dotProduct < -0.3f; // Agent is behind us
                    
                    // Also check if the other agent is heading towards us
                    Vector3 otherForward = otherPF.transform.forward;
                    float approachDot = Vector3.Dot(otherForward, -toOther.normalized);
                    bool isApproaching = approachDot > 0.3f; // Other agent is heading towards us
                    
                    if (isFromBehind && isApproaching)
                    {
                        return (true, otherPF.GetCurrentSpeed(), distance, otherPF.transform, true, false);
                    }
                    else
                    {
                        // Check if detection is from the side
                        float forwardDotPF = Vector3.Dot(cachedForward, toOther.normalized);
                        bool isSideDetectionPF = Mathf.Abs(forwardDotPF) < 0.5f; // Side if not mostly in front or behind
                        return (true, otherPF.GetCurrentSpeed(), distance, otherPF.transform, false, isSideDetectionPF);
                    }
                }
            }
        }

        // Raycast in front and rear (using raycastDistance)
        RaycastHit hit;
        
        // Front raycast
        if (Physics.Raycast(origin, cachedForward, out hit, raycastDistance))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false, false);
        }
        
        // Rear raycast - agents behind us approaching
        if (Physics.Raycast(origin, -cachedForward, out hit, raycastDistance))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, true, false);
        }
        
        // Side raycasts (using raycastWidth) - mark as side detection
        if (Physics.Raycast(origin, cachedRight, out hit, raycastWidth))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false, true);
        }
        
        if (Physics.Raycast(origin, -cachedRight, out hit, raycastWidth))
        {
            var result = CheckHitForAgent(hit);
            if (result.detected) return (result.detected, result.otherSpeed, result.distance, result.otherTransform, false, true);
        }

        return (false, 0f, 0f, null, false, false);
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
        if (otherACO != null && otherACO != this && otherACO.enabled)
        {
            return (true, otherACO.CalculateSpeed(), hit.distance, otherACO.transform);
        }

        PathfindingTester otherPF = hit.collider.GetComponentInParent<PathfindingTester>();
        if (otherPF != null && otherPF.enabled)
        {
            return (true, otherPF.GetCurrentSpeed(), hit.distance, otherPF.transform);
        }
        
        return (false, 0f, 0f, null);
    }

    /// <summary>
    /// Determine if this agent should yield based on speed comparison.
    /// Slower agent yields to faster agent.
    /// </summary>
    private bool ShouldYieldTo(float otherSpeed)
    {
        float mySpeed = CalculateSpeed();
        // Yield if the other agent is faster (with small tolerance)
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
    /// Execute collision avoidance: slow down, move aside, wait for 3 seconds.
    /// For side collisions, the slower agent moves back by assigned distance.
    /// Returns: (shouldStop, speedMultiplier, sideStepDir, isFirstYield)
    /// </summary>
    private (bool shouldStop, float speedMultiplier, Vector3 sideStepDir, bool isFirstYield) ProcessCollisionAvoidance()
    {
        float dt = Time.deltaTime;
        float mySpeed = CalculateSpeed();

        if (isYielding)
        {
            yieldTimer += dt;
            
            // Wait for exactly yieldWaitTime (3 seconds by default) before resuming
            if (yieldTimer >= yieldWaitTime)
            {
                isYielding = false;
                detectedAgent = null;
                yieldTimer = 0f;
                currentStatus = "Resuming path";
                if (stats != null) stats.deliveryStatus = "ACO: Resuming";
                Debug.Log($"[ACOTester] {gameObject.name}: Wait complete ({yieldWaitTime}s), resuming path");
                return (false, 1f, Vector3.zero, false);
            }

            // Still waiting - complete stop, no movement
            currentStatus = $"Waiting... ({yieldWaitTime - yieldTimer:F1}s)";
            if (stats != null) stats.deliveryStatus = currentStatus;
            return (true, 0f, Vector3.zero, false);
        }

        var (detected, otherSpeed, distance, otherTransform, isFromBehind, isFromSide) = RaycastForAgentAhead();
        
        float maxDetectRange = Mathf.Max(raycastDistance, raycastWidth);
        if (detected && distance < maxDetectRange)
        {
            // SIDE COLLISION - slower agent moves back by assigned distance
            if (isFromSide && ShouldYieldTo(otherSpeed))
            {
                isYielding = true;
                detectedAgent = otherTransform;
                yieldTimer = 0f;
                
                // Move back by assigned distance
                MoveBackByDistance();
                
                // Get other agent name for HUD message
                string otherName = otherTransform.root.name;
                string myName = stats != null ? stats.agentName : gameObject.name;
                AgentStatsSource.lastCollisionMessage = $"{myName} moved back (side collision) for {otherName}";
                
                currentStatus = $"MOVED BACK (side) - Waiting {yieldWaitTime}s";
                if (stats != null) stats.deliveryStatus = currentStatus;
                Debug.Log($"[ACOTester] {gameObject.name}: SIDE COLLISION! Moving back {sideStepBackDistance} units and waiting {yieldWaitTime}s (my speed: {mySpeed:F1}, other: {otherSpeed:F1})");

                return (true, 0f, Vector3.zero, false);
            }
            // If faster agent is approaching from behind, step aside immediately
            else if (isFromBehind && ShouldYieldTo(otherSpeed))
            {
                isYielding = true;
                detectedAgent = otherTransform;
                yieldTimer = 0f;
                yieldTargetPosition = CalculateSideStepPosition(otherTransform);
                
                // Get other agent name for HUD message
                string otherName = otherTransform.root.name;
                string myName = stats != null ? stats.agentName : gameObject.name;
                AgentStatsSource.lastCollisionMessage = $"{myName} yielded to {otherName} (rear)";
                
                currentStatus = $"STEPPED ASIDE (rear approach) - Waiting {yieldWaitTime}s";
                if (stats != null) stats.deliveryStatus = currentStatus;
                Debug.Log($"[ACOTester] {gameObject.name}: FASTER AGENT APPROACHING FROM BEHIND! Stepping aside by {sideStepDistance} units and waiting {yieldWaitTime}s (my speed: {mySpeed:F1}, other: {otherSpeed:F1})");

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
                
                currentStatus = $"STEPPED ASIDE - Waiting {yieldWaitTime}s";
                if (stats != null) stats.deliveryStatus = currentStatus;
                Debug.Log($"[ACOTester] {gameObject.name}: STEPPING ASIDE by {sideStepDistance} units and waiting {yieldWaitTime}s for faster agent (my speed: {mySpeed:F1}, other: {otherSpeed:F1})");

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

    /// <summary>
    /// Move the agent back by the assigned sideStepBackDistance.
    /// Called when a side collision is detected and this agent should yield.
    /// </summary>
    private void MoveBackByDistance()
    {
        // Move back by the assigned step back distance (opposite to forward direction)
        Vector3 backDirection = -transform.forward;
        Vector3 newPosition = transform.position + backDirection * sideStepBackDistance;
        
        // Keep the same Y position to avoid clipping through ground
        newPosition.y = transform.position.y;
        
        // Teleport to the new position
        transform.position = newPosition;
        yieldTargetPosition = newPosition;
        
        Debug.Log($"[ACOTester] {gameObject.name}: Moved back {sideStepBackDistance} units to {newPosition}");
    }

    #endregion

    #region Gizmos and Debug Drawing

    void OnDrawGizmos()
    {
        // Draw collision detection rays
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
        
        // Draw ACO route
        if (acoRoute != null && acoRoute.Count > 0)
        {
            Gizmos.color = carColor;
            foreach (ACOConnection conn in acoRoute)
            {
                if (conn.FromNode != null && conn.ToNode != null)
                {
                    Gizmos.DrawLine(
                        conn.FromNode.transform.position + offsetY,
                        conn.ToNode.transform.position + offsetY
                    );
                }
            }
        }

        // Draw current path
        if (currentPath != null && currentPath.Count > 0)
        {
            Gizmos.color = carColor;
            foreach (ACOConnection conn in currentPath)
            {
                if (conn.FromNode != null && conn.ToNode != null)
                {
                    Gizmos.DrawLine(
                        conn.FromNode.transform.position + Vector3.up * 0.6f,
                        conn.ToNode.transform.position + Vector3.up * 0.6f
                    );
                }
            }
        }

        // Draw goal nodes
        if (goalNodes != null)
        {
            Gizmos.color = Color.yellow;
            foreach (var goal in goalNodes)
            {
                if (goal != null)
                    Gizmos.DrawWireSphere(goal.transform.position, 1f);
            }
        }

        // Draw start node
        if (startNode != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(startNode.transform.position, 1.2f);
        }
    }
    
    #endregion
}
