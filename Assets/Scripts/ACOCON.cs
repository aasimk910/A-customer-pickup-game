using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Controller - Core Ant Colony Optimization algorithm.
/// All parameters are configurable via the Inspector through ACOManager.
/// Optimized with connection caching and HashSet for visited nodes.
/// </summary>
public class ACOCON
{
    #region ACO Parameters
    
    // ACO Parameters (set by ACOManager) - using auto-properties
    public float DefaultPheromone { get; set; } = 1.0f;
    public float Alpha { get; set; } = 1.0f;
    public float Beta { get; set; } = 2.0f;
    public float EvaporationFactor { get; set; } = 0.5f;
    public float Q { get; set; } = 100f;
    
    #endregion

    #region Private State

    // Ants moving through the graph
    public List<ACOAnt> Ants = new List<ACOAnt>();

    // The generated route
    private List<ACOConnection> MyRoute = new List<ACOConnection>();

    // Cached connection lookup for O(1) access - built once per ACO run
    private Dictionary<GameObject, List<ACOConnection>> connectionCache;

    // Reusable list to avoid allocations
    private List<ACOConnection> tempConnectionList = new List<ACOConnection>();
    
    #endregion

    #region Constructor

    public ACOCON()
    {
    }
    
    #endregion

    #region Connection Cache

    /// <summary>
    /// Build connection cache for O(1) lookup by FromNode.
    /// </summary>
    private void BuildConnectionCache(List<ACOConnection> connections)
    {
        if (connectionCache == null)
            connectionCache = new Dictionary<GameObject, List<ACOConnection>>();
        else
            connectionCache.Clear();

        for (int i = 0; i < connections.Count; i++)
        {
            var conn = connections[i];
            if (conn.FromNode == null) continue;
            
            if (!connectionCache.TryGetValue(conn.FromNode, out List<ACOConnection> list))
            {
                list = new List<ACOConnection>();
                connectionCache[conn.FromNode] = list;
            }
            list.Add(conn);
        }
    }
    
    #endregion

    #region ACO Algorithm

    /// <summary>
    /// Run ACO algorithm to find optimal route through goal nodes.
    /// </summary>
    /// <param name="IterationThreshold">Max number of iterations</param>
    /// <param name="TotalNumAnts">Total number of ants in simulation</param>
    /// <param name="WaypointNodes">All waypoint nodes (goal nodes)</param>
    /// <param name="Connections">Connections between nodes</param>
    /// <param name="StartNode">Starting node</param>
    /// <param name="MaxPathLength">Maximum path length</param>
    /// <returns>Optimal route as list of connections</returns>
    public List<ACOConnection> ACO(int IterationThreshold, int TotalNumAnts,
        GameObject[] WaypointNodes, List<ACOConnection> Connections,
        GameObject StartNode, int MaxPathLength)
    {
        if (StartNode == null)
        {
            Debug.Log("[ACOCON] No Start node.");
            return new List<ACOConnection>();
        }

        if (WaypointNodes.Length == 0)
        {
            Debug.Log("[ACOCON] No waypoint nodes.");
            return new List<ACOConnection>();
        }

        // Build connection cache for O(1) lookups
        BuildConnectionCache(Connections);

        // The node the ant is currently at
        GameObject currentNode;

        // Use HashSet for O(1) visited checks
        HashSet<GameObject> VisitedNodes = new HashSet<GameObject>();
        int waypointCount = WaypointNodes.Length;

        for (int i = 0; i < IterationThreshold; i++)
        {
            // Clear ants from previous iterations
            Ants.Clear();

            for (int i2 = 0; i2 < TotalNumAnts; i2++)
            {
                ACOAnt aAnt = new ACOAnt();

                // Randomly choose start node
                currentNode = WaypointNodes[Random.Range(0, waypointCount)];
                aAnt.StartNode = currentNode;
                VisitedNodes.Clear();

                // Keep moving through nodes until visited them all
                while (VisitedNodes.Count < waypointCount)
                {
                    // Get all connections from node that haven't been visited (using cache)
                    GetConnectionsFromNodeAndNotVisited(currentNode, VisitedNodes, tempConnectionList);

                    if (tempConnectionList.Count == 0)
                        break;

                    // Sum the product of pheromone level and visibility factor
                    float TotalPheromoneAndVisibility = CalculateTotalPheromoneAndVisibility(tempConnectionList);

                    // Calculate path probabilities
                    for (int j = 0; j < tempConnectionList.Count; j++)
                    {
                        ACOConnection aConnection = tempConnectionList[j];
                        float PathProbability = (Mathf.Pow(aConnection.PheromoneLevel, Alpha) *
                            Mathf.Pow((1f / aConnection.Distance), Beta));
                        PathProbability = PathProbability / Mathf.Max(TotalPheromoneAndVisibility, 0.0001f);
                        aConnection.PathProbability = PathProbability;
                    }

                    // Select path with largest probability
                    ACOConnection largestProbability = tempConnectionList[0];
                    for (int i3 = 1; i3 < tempConnectionList.Count; i3++)
                    {
                        if (tempConnectionList[i3].PathProbability > largestProbability.PathProbability)
                        {
                            largestProbability = tempConnectionList[i3];
                        }
                        else if (Mathf.Approximately(tempConnectionList[i3].PathProbability,
                            largestProbability.PathProbability))
                        {
                            // Choose shortest if probabilities are equal
                            if (tempConnectionList[i3].Distance < largestProbability.Distance)
                            {
                                largestProbability = tempConnectionList[i3];
                            }
                        }
                    }

                    // Move to next node
                    VisitedNodes.Add(currentNode);
                    currentNode = largestProbability.ToNode;
                    aAnt.AddTravelledConnection(largestProbability);
                    aAnt.AddAntTourLength(largestProbability.Distance);
                }

                // Add connection back to start node
                if (connectionCache.TryGetValue(currentNode, out List<ACOConnection> fromConnections))
                {
                    for (int j = 0; j < fromConnections.Count; j++)
                    {
                        if (fromConnections[j].ToNode == aAnt.StartNode)
                        {
                            aAnt.AddTravelledConnection(fromConnections[j]);
                            aAnt.AddAntTourLength(fromConnections[j].Distance);
                            break;
                        }
                    }
                }

                Ants.Add(aAnt);
            }

            // Update pheromone levels
            for (int c = 0; c < Connections.Count; c++)
            {
                ACOConnection aConnection = Connections[c];
                float Sum = 0;
                for (int a = 0; a < Ants.Count; a++)
                {
                    ACOAnt TmpAnt = Ants[a];
                    var travelledConnections = TmpAnt.AntTravelledConnections;
                    for (int t = 0; t < travelledConnections.Count; t++)
                    {
                        if (aConnection == travelledConnections[t])
                        {
                            Sum += Q / Mathf.Max(TmpAnt.AntTourLength, 0.001f);
                        }
                    }
                }

                // Pheromone update formula
                float NewPheromoneLevel = (1f - EvaporationFactor) * aConnection.PheromoneLevel + Sum;
                aConnection.PheromoneLevel = Mathf.Max(NewPheromoneLevel, 0.001f);
                aConnection.PathProbability = 0;
            }
        }

        // Generate and return the route
        MyRoute = GenerateRoute(StartNode, MaxPathLength, Connections);
        return MyRoute;
    }
    
    #endregion

    #region Connection Query Helpers

    /// <summary>
    /// Get all connections from a node (uses cache if available, otherwise falls back to linear search).
    /// </summary>
    public List<ACOConnection> AllConnectionsFromNode(GameObject FromNode, List<ACOConnection> Connections)
    {
        // Try cache first
        if (connectionCache != null && connectionCache.TryGetValue(FromNode, out List<ACOConnection> cached))
        {
            return cached;
        }
        
        // Fallback to linear search (for external calls without cache)
        List<ACOConnection> ConnectionsFromNode = new List<ACOConnection>();
        for (int i = 0; i < Connections.Count; i++)
        {
            if (Connections[i].FromNode == FromNode)
            {
                ConnectionsFromNode.Add(Connections[i]);
            }
        }
        return ConnectionsFromNode;
    }

    /// <summary>
    /// Get all connections from a node that haven't been visited (uses cache, writes to output list to avoid allocation).
    /// </summary>
    private void GetConnectionsFromNodeAndNotVisited(GameObject FromNode, HashSet<GameObject> VisitedList, List<ACOConnection> output)
    {
        output.Clear();
        
        if (connectionCache != null && connectionCache.TryGetValue(FromNode, out List<ACOConnection> cached))
        {
            for (int i = 0; i < cached.Count; i++)
            {
                if (!VisitedList.Contains(cached[i].ToNode))
                {
                    output.Add(cached[i]);
                }
            }
        }
    }
    
    #endregion

    #region Probability Calculation

    /// <summary>
    /// Calculate total pheromone and visibility for probability calculation.
    /// </summary>
    private float CalculateTotalPheromoneAndVisibility(List<ACOConnection> ConnectionsFromNodeAndNotVisited)
    {
        float TotalPheromoneAndVisibility = 0;
        for (int i = 0; i < ConnectionsFromNodeAndNotVisited.Count; i++)
        {
            ACOConnection aConnection = ConnectionsFromNodeAndNotVisited[i];
            TotalPheromoneAndVisibility +=
                (Mathf.Pow(aConnection.PheromoneLevel, Alpha) *
                 Mathf.Pow((1f / aConnection.Distance), Beta));
        }
        return TotalPheromoneAndVisibility;
    }
    
    #endregion

    #region Route Generation

    /// <summary>
    /// Generate route by following highest pheromone connections.
    /// </summary>
    public List<ACOConnection> GenerateRoute(GameObject StartNode, int MaxPath, List<ACOConnection> Connections)
    {
        // Ensure cache is built
        if (connectionCache == null || connectionCache.Count == 0)
            BuildConnectionCache(Connections);
            
        GameObject CurrentNode = StartNode;
        List<ACOConnection> Route = new List<ACOConnection>();
        HashSet<GameObject> visited = new HashSet<GameObject>();
        int PathCount = 0;

        while (CurrentNode != null && PathCount < MaxPath)
        {
            visited.Add(CurrentNode);
            
            if (!connectionCache.TryGetValue(CurrentNode, out List<ACOConnection> AllFromConnections) || 
                AllFromConnections.Count == 0)
            {
                break;
            }

            // Find highest pheromone connection that hasn't been visited
            ACOConnection HighestPheromoneConnection = null;
            float highestPheromone = -1f;

            for (int i = 0; i < AllFromConnections.Count; i++)
            {
                ACOConnection aConnection = AllFromConnections[i];
                if (!visited.Contains(aConnection.ToNode) || aConnection.ToNode == StartNode)
                {
                    if (aConnection.PheromoneLevel > highestPheromone)
                    {
                        highestPheromone = aConnection.PheromoneLevel;
                        HighestPheromoneConnection = aConnection;
                    }
                }
            }

            if (HighestPheromoneConnection != null)
            {
                Route.Add(HighestPheromoneConnection);
                CurrentNode = HighestPheromoneConnection.ToNode;

                // Check if returned to start
                if (CurrentNode == StartNode)
                {
                    break;
                }
            }
            else
            {
                break;
            }

            PathCount++;
        }

        return Route;
    }

    /// <summary>
    /// Find path from start to a specific goal node.
    /// </summary>
    public List<ACOConnection> FindPathToGoal(GameObject StartNode, GameObject GoalNode, 
        List<ACOConnection> Connections, int MaxPath = 100)
    {
        // Ensure cache is built
        if (connectionCache == null || connectionCache.Count == 0)
            BuildConnectionCache(Connections);
            
        GameObject CurrentNode = StartNode;
        List<ACOConnection> Route = new List<ACOConnection>();
        HashSet<GameObject> visited = new HashSet<GameObject>();
        int PathCount = 0;

        while (CurrentNode != null && CurrentNode != GoalNode && PathCount < MaxPath)
        {
            visited.Add(CurrentNode);
            
            if (!connectionCache.TryGetValue(CurrentNode, out List<ACOConnection> AllFromConnections) ||
                AllFromConnections.Count == 0)
            {
                break;
            }

            ACOConnection BestConnection = null;
            float bestScore = -1f;
            Vector3 goalPos = GoalNode.transform.position;

            for (int i = 0; i < AllFromConnections.Count; i++)
            {
                ACOConnection aConnection = AllFromConnections[i];
                if (!visited.Contains(aConnection.ToNode))
                {
                    // Score based on pheromone and distance to goal
                    float distToGoalSqr = (aConnection.ToNode.transform.position - goalPos).sqrMagnitude;
                    float distToGoal = Mathf.Sqrt(distToGoalSqr);
                    float score = aConnection.PheromoneLevel / Mathf.Max(distToGoal, 0.1f);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        BestConnection = aConnection;
                    }
                }
            }

            if (BestConnection != null)
            {
                Route.Add(BestConnection);
                CurrentNode = BestConnection.ToNode;
            }
            else
            {
                break;
            }

            PathCount++;
        }

        return Route;
    }
    
    #endregion
}
