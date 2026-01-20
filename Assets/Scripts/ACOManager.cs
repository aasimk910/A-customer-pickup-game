using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Manager - Manages ACO parameters and provides access to the algorithm.
/// All ACO parameters are configurable via the Unity Inspector.
/// </summary>
public class ACOManager : MonoBehaviour
{
    #region Inspector Fields - ACO Parameters
    
    [Header("ACO Parameters (Configurable via Inspector)")]
    [Tooltip("Importance of pheromone trail (α)")]
    [Range(0.1f, 5f)]
    public float alpha = 1.0f;

    [Tooltip("Importance of heuristic distance (β)")]
    [Range(0.1f, 5f)]
    public float beta = 2.0f;

    [Tooltip("Pheromone deposit constant (Q)")]
    [Range(1f, 1000f)]
    public float qValue = 100f;

    [Tooltip("Initial pheromone level on all connections")]
    [Range(0.01f, 10f)]
    public float defaultPheromone = 1.0f;

    [Tooltip("Pheromone evaporation rate (ρ) - 0 to 1")]
    [Range(0f, 1f)]
    public float evaporationFactor = 0.5f;
    
    #endregion

    #region Inspector Fields - Simulation Settings

    [Header("ACO Simulation Settings")]
    [Tooltip("Number of iterations for ACO algorithm")]
    [Range(10, 200)]
    public int iterationThreshold = 50;

    [Tooltip("Number of ants in simulation")]
    [Range(5, 100)]
    public int totalNumAnts = 25;

    [Tooltip("Maximum path length")]
    [Range(5, 100)]
    public int maxPathLength = 50;
    
    #endregion

    #region Public Properties
    public float Alpha => alpha;
    public float Beta => beta;
    public float QValue => qValue;
    public float DefaultPheromone => defaultPheromone;
    public float EvaporationFactor => evaporationFactor;
    
    #endregion

    #region Private State

    // The ACO Controller
    private ACOCON acoController;
    public ACOCON ACOController => acoController;

    // Connections between nodes
    private List<ACOConnection> connections = new List<ACOConnection>();
    public List<ACOConnection> Connections => connections;

    // All waypoints (goal nodes)
    private List<GameObject> waypointNodes = new List<GameObject>();
    public List<GameObject> WaypointNodes => waypointNodes;

    // Singleton pattern
    private static ACOManager instance;
    public static ACOManager Instance => instance;

    // Is initialized flag
    private bool isInitialized = false;
    public bool IsInitialized => isInitialized;
    
    #endregion

    #region Unity Lifecycle Methods

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Destroy(gameObject);

        acoController = new ACOCON();
        UpdateACOParameters();
    }

    void Start()
    {
        InitializeGraph();
    }
    
    #endregion

    #region Parameter Management

    /// <summary>
    /// Update ACO controller with current Inspector values.
    /// </summary>
    public void UpdateACOParameters()
    {
        if (acoController == null)
            acoController = new ACOCON();

        acoController.Alpha = alpha;
        acoController.Beta = beta;
        acoController.Q = qValue;
        acoController.DefaultPheromone = defaultPheromone;
        acoController.EvaporationFactor = evaporationFactor;
    }
    
    #endregion

    #region Graph Initialization

    /// <summary>
    /// Initialize the graph from waypoints tagged as "Waypoint".
    /// Only includes waypoints marked as Goal type.
    /// </summary>
    public void InitializeGraph()
    {
        connections.Clear();
        waypointNodes.Clear();

        // Find all waypoints with the Waypoint tag
        GameObject[] GameObjectsWithWaypointTag = GameObject.FindGameObjectsWithTag("Waypoint");

        foreach (GameObject waypoint in GameObjectsWithWaypointTag)
        {
            VisGraphWaypointManager waypointManager = waypoint.GetComponent<VisGraphWaypointManager>();
            if (waypointManager != null)
            {
                // Only add Goal type waypoints for ACO
                if (waypointManager.IsGoal)
                {
                    waypointNodes.Add(waypoint);
                }
            }
        }

        // Create connections between waypoints
        foreach (GameObject waypoint in waypointNodes)
        {
            VisGraphWaypointManager waypointManager = waypoint.GetComponent<VisGraphWaypointManager>();
            if (waypointManager == null) continue;

            foreach (VisGraphConnection visConnection in waypointManager.Connections)
            {
                if (visConnection.ToNode != null)
                {
                    ACOConnection aConnection = new ACOConnection();
                    aConnection.SetConnection(waypoint, visConnection.ToNode, defaultPheromone);
                    connections.Add(aConnection);
                }
            }
        }

        isInitialized = true;
        Debug.Log($"[ACOManager] Initialized with {waypointNodes.Count} goal waypoints and {connections.Count} connections.");
    }
    
    #endregion

    #region ACO Algorithm

    /// <summary>
    /// Run the ACO algorithm to find optimal route.
    /// </summary>
    public List<ACOConnection> RunACO(GameObject startNode)
    {
        if (!isInitialized)
            InitializeGraph();

        UpdateACOParameters();

        if (waypointNodes.Count < 2)
        {
            Debug.LogWarning("[ACOManager] Need at least 2 goal waypoints for ACO.");
            return new List<ACOConnection>();
        }

        return acoController.ACO(
            iterationThreshold,
            totalNumAnts,
            waypointNodes.ToArray(),
            connections,
            startNode,
            maxPathLength
        );
    }

    /// <summary>
    /// Find path from current position to a specific goal.
    /// </summary>
    public List<ACOConnection> FindPathToGoal(GameObject startNode, GameObject goalNode)
    {
        if (!isInitialized)
            InitializeGraph();

        return acoController.FindPathToGoal(startNode, goalNode, connections, maxPathLength);
    }
    
    #endregion

    #region Utility Methods

    /// <summary>
    /// Get all connections from a specific node.
    /// </summary>
    public List<ACOConnection> GetConnectionsFromNode(GameObject node)
    {
        return acoController.AllConnectionsFromNode(node, connections);
    }

    /// <summary>
    /// Find the nearest waypoint to a position.
    /// Uses sqrMagnitude for faster distance comparison.
    /// </summary>
    public GameObject FindNearestWaypoint(Vector3 position)
    {
        GameObject nearest = null;
        float nearestDistSqr = float.MaxValue;

        for (int i = 0; i < waypointNodes.Count; i++)
        {
            var waypoint = waypointNodes[i];
            if (waypoint == null) continue;

            float distSqr = (position - waypoint.transform.position).sqrMagnitude;
            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = waypoint;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Reset all pheromone levels to default.
    /// </summary>
    public void ResetPheromones()
    {
        for (int i = 0; i < connections.Count; i++)
        {
            connections[i].PheromoneLevel = defaultPheromone;
        }
    }
    
    #endregion

    #region Editor Support

    void OnValidate()
    {
        // Update ACO parameters when changed in Inspector
        if (acoController != null)
            UpdateACOParameters();
    }
    
    #endregion
}
