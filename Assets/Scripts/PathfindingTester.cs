using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindingTester : MonoBehaviour
{
    // The A* manager.
    private AStarManager AStarManager = new AStarManager();
    // List of possible waypoints.
    private List<GameObject> Waypoints = new List<GameObject>();
    // List of waypoint map connections. Represents a path.
    private List<Connection> ConnectionArray = new List<Connection>();

    // The start (taxi stand), pickup and end (destination) nodes.
    [SerializeField] private GameObject start;
    [SerializeField] private GameObject pickup;
    [SerializeField] private GameObject end;

    // Single customer object that starts at pickup.
    [SerializeField] private GameObject customer;

    // Where the customer will sit in the car.
    [SerializeField] private Transform passengerSeat;   // drag a child object of the car here
    [SerializeField] private float seatMoveDuration = 1.5f;

    // Debug line offset.
    private Vector3 OffSet = new Vector3(0, 0.3f, 0);

    // Movement variables.
    [SerializeField] private float currentSpeed = 8f;   // base speed
    [SerializeField] private float turnSpeed = 5f;      // how fast the car rotates
    [SerializeField] private bool agentMove = true;
    [SerializeField] private float waitAtPickupSeconds = 3f;

    private int currentTarget = 0;
    private Vector3 currentTargetPos;

    // For knowing when we are in each leg of the trip.
    private int startToPickupCount = 0;
    private int pickupToEndCount = 0;
    private int endToStartCount = 0;

    // Passenger state.
    private bool hasPassenger = false;

    // Store original offset of customer relative to pickup waypoint.
    private Vector3 customerOffsetFromPickup;

    // -------- Performance measures --------
    private float elapsedTime = 0f;
    private float totalDistanceTravelled = 0f;
    private float currentSpeedValue = 0f;

    // Timer control
    private bool timerRunning = true;

    // -------- Status text --------
    private string statusText = "Heading to pickup";

    // NEW: link to stats for THIS agent
    private AgentStatsSource stats;

    void Start()
    {
        // NEW: grab the stats component on this car (agent)
        stats = GetComponent<AgentStatsSource>();
        if (stats != null)
        {
            stats.deliveryStatus = "Heading to pickup";
            // If you didn't type a custom name in Inspector, use GameObject name
            if (string.IsNullOrWhiteSpace(stats.agentName))
                stats.agentName = gameObject.name;
        }

        // Basic checks.
        if (start == null || pickup == null || end == null)
        {
            Debug.Log("Start, pickup or end waypoints are not assigned.");
            return;
        }

        if (passengerSeat == null)
        {
            Debug.LogWarning("Passenger seat is not assigned. Customer will still disappear at pickup.");
        }

        VisGraphWaypointManager tmpWpM = start.GetComponent<VisGraphWaypointManager>();
        if (tmpWpM == null)
        {
            Debug.Log("Start is not a waypoint.");
            return;
        }

        tmpWpM = pickup.GetComponent<VisGraphWaypointManager>();
        if (tmpWpM == null)
        {
            Debug.Log("Pickup is not a waypoint.");
            return;
        }

        tmpWpM = end.GetComponent<VisGraphWaypointManager>();
        if (tmpWpM == null)
        {
            Debug.Log("End is not a waypoint.");
            return;
        }

        // Setup customer initial position info.
        if (customer != null && pickup != null)
        {
            customerOffsetFromPickup = customer.transform.position - pickup.transform.position;
            customer.SetActive(true);
        }

        // Place car at the start initially.
        transform.position = start.transform.position;

        // Find all the waypoints in the level.
        GameObject[] gameObjectsWithWaypointTag = GameObject.FindGameObjectsWithTag("Waypoint");
        foreach (GameObject waypoint in gameObjectsWithWaypointTag)
        {
            VisGraphWaypointManager tmpWaypointMan = waypoint.GetComponent<VisGraphWaypointManager>();
            if (tmpWaypointMan)
            {
                Waypoints.Add(waypoint);
            }
        }

        // Create connections in the A* graph.
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

        // --- Build the full route: start -> pickup -> end -> start ---

        // 1) start -> pickup
        List<Connection> pathStartToPickup = AStarManager.PathfindAStar(start, pickup);

        // 2) pickup -> end
        List<Connection> pathPickupToEnd = AStarManager.PathfindAStar(pickup, end);

        // 3) end -> start
        List<Connection> pathEndToStart = AStarManager.PathfindAStar(end, start);

        // Combine them into one big loop path.
        ConnectionArray.Clear();
        ConnectionArray.AddRange(pathStartToPickup);
        ConnectionArray.AddRange(pathPickupToEnd);
        ConnectionArray.AddRange(pathEndToStart);

        if (ConnectionArray.Count == 0)
        {
            Debug.Log("Warning, no path for the taxi route.");
            return;
        }

        // Store lengths so we know where each leg ends.
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
        float deltaTime = Time.deltaTime;

        if (timerRunning)
            elapsedTime += deltaTime;

        Vector3 prevPos = transform.position;

        if (agentMove && ConnectionArray.Count > 0)
        {
            if (currentTarget < 0) currentTarget = 0;
            if (currentTarget >= ConnectionArray.Count) currentTarget = ConnectionArray.Count - 1;

            float moveSpeed = currentSpeed;

            if (hasPassenger)
                moveSpeed = currentSpeed * 0.9f;

            currentTargetPos = ConnectionArray[currentTarget].ToNode.transform.position;
            currentTargetPos.y = transform.position.y;

            Vector3 direction = currentTargetPos - transform.position;
            float distance = direction.magnitude;

            direction.y = 0;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);
            }

            if (distance > 0.0001f)
            {
                Vector3 normDirection = direction.normalized;
                transform.position += normDirection * moveSpeed * deltaTime;
            }

            float newDistance = (currentTargetPos - transform.position).magnitude;

            if (newDistance < 1f)
            {
                currentTarget++;

                // Finished leg (start -> pickup): trigger pickup once.
                if (!hasPassenger && currentTarget == startToPickupCount)
                {
                    hasPassenger = true;
                    Debug.Log("Reached PICKUP. Passenger getting in...");
                    StartCoroutine(PickupSequence());
                }

                // End: stop back at start.
                if (currentTarget >= ConnectionArray.Count)
                {
                    currentTarget = ConnectionArray.Count - 1;
                    agentMove = false;
                    statusText = "Returned to start";
                    timerRunning = false;

                    if (stats != null)
                        stats.deliveryStatus = "Returned to start";

                    Debug.Log("Taxi returned to START and stopped. Timer stopped.");
                }
            }
        }

        // -------- Update performance measures --------
        Vector3 currentPos = transform.position;
        float frameDistance = Vector3.Distance(prevPos, currentPos);
        totalDistanceTravelled += frameDistance;
        currentSpeedValue = (deltaTime > 0f) ? frameDistance / deltaTime : 0f;
    }

    private IEnumerator MoveCustomerToSeat()
    {
        if (customer == null || passengerSeat == null)
        {
            if (customer != null)
                customer.SetActive(false);
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

    // NEW: increment customer count for THIS agent when pickup happens
    private IEnumerator PickupSequence()
    {
        agentMove = false;
        statusText = "Loading passenger";

        if (stats != null)
        {
            stats.packageCount += 1;              // this is your "Customer count"
            stats.deliveryStatus = "Loading passenger";
        }

        Debug.Log("Waiting at pickup for " + waitAtPickupSeconds + " seconds (including sit animation)...");

        yield return new WaitForSeconds(2f);

        yield return StartCoroutine(MoveCustomerToSeat());
        Debug.Log("Customer seated in the car.");

        float remaining = Mathf.Max(0f, waitAtPickupSeconds - 2f);
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        statusText = "Passenger picked up";
        if (stats != null)
            stats.deliveryStatus = "Passenger picked up";

        agentMove = true;
    }
}
