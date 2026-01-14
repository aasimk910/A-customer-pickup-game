using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Controller - Core Ant Colony Optimization algorithm.
/// All parameters are configurable via the Inspector through ACOManager.
/// Based on reference implementation with proper pheromone updates.
/// </summary>
public class ACOCON
{
    // ACO Parameters (set by ACOManager)
    private float defaultPheromone = 1.0f;
    public float DefaultPheromone
    {
        get { return defaultPheromone; }
        set { defaultPheromone = value; }
    }

    private float alpha = 1.0f;
    public float Alpha
    {
        get { return alpha; }
        set { alpha = value; }
    }

    private float beta = 2.0f;
    public float Beta
    {
        get { return beta; }
        set { beta = value; }
    }

    // Evaporation factor: 0 ≤ EvaporationFactor ≤ 1
    private float evaporationFactor = 0.5f;
    public float EvaporationFactor
    {
        get { return evaporationFactor; }
        set { evaporationFactor = value; }
    }

    // Q is the pheromone deposit constant
    private float q = 100f;
    public float Q
    {
        get { return q; }
        set { q = value; }
    }

    // Ants moving through the graph
    public List<ACOAnt> Ants = new List<ACOAnt>();

    // The generated route
    private List<ACOConnection> MyRoute = new List<ACOConnection>();

    public ACOCON()
    {
    }

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

        // The node the ant is currently at
        GameObject currentNode;

        // A list of all visited nodes
        List<GameObject> VisitedNodes = new List<GameObject>();

        for (int i = 0; i < IterationThreshold; i++)
        {
            // Clear ants from previous iterations
            Ants.Clear();

            for (int i2 = 0; i2 < TotalNumAnts; i2++)
            {
                ACOAnt aAnt = new ACOAnt();

                // Randomly choose start node
                currentNode = WaypointNodes[Random.Range(0, WaypointNodes.Length)];
                aAnt.StartNode = currentNode;
                VisitedNodes.Clear();

                // Keep moving through nodes until visited them all
                while (VisitedNodes.Count < WaypointNodes.Length)
                {
                    // Get all connections from node that haven't been visited
                    List<ACOConnection> ConnectionsFromNodeAndNotVisited =
                        AllConnectionsFromNodeAndNotVisited(currentNode, Connections, VisitedNodes);

                    // Sum the product of pheromone level and visibility factor
                    float TotalPheromoneAndVisibility =
                        CalculateTotalPheromoneAndVisibility(ConnectionsFromNodeAndNotVisited);

                    // Calculate path probabilities
                    foreach (ACOConnection aConnection in ConnectionsFromNodeAndNotVisited)
                    {
                        float PathProbability = (Mathf.Pow(aConnection.PheromoneLevel, Alpha) *
                            Mathf.Pow((1 / aConnection.Distance), Beta));
                        PathProbability = PathProbability / Mathf.Max(TotalPheromoneAndVisibility, 0.0001f);
                        aConnection.PathProbability = PathProbability;
                    }

                    // Select path with largest probability
                    ACOConnection largestProbability = null;
                    if (ConnectionsFromNodeAndNotVisited.Count > 0)
                    {
                        largestProbability = ConnectionsFromNodeAndNotVisited[0];
                        for (int i3 = 1; i3 < ConnectionsFromNodeAndNotVisited.Count; i3++)
                        {
                            if (ConnectionsFromNodeAndNotVisited[i3].PathProbability >
                                largestProbability.PathProbability)
                            {
                                largestProbability = ConnectionsFromNodeAndNotVisited[i3];
                            }
                            else if (Mathf.Approximately(ConnectionsFromNodeAndNotVisited[i3].PathProbability,
                                largestProbability.PathProbability))
                            {
                                // Choose shortest if probabilities are equal
                                if (ConnectionsFromNodeAndNotVisited[i3].Distance <
                                    largestProbability.Distance)
                                {
                                    largestProbability = ConnectionsFromNodeAndNotVisited[i3];
                                }
                            }
                        }
                    }

                    // Move to next node
                    VisitedNodes.Add(currentNode);
                    if (largestProbability != null)
                    {
                        currentNode = largestProbability.ToNode;
                        aAnt.AddTravelledConnection(largestProbability);
                        aAnt.AddAntTourLength(largestProbability.Distance);
                    }
                    else
                    {
                        break; // No valid connections
                    }
                }

                // Add connection back to start node
                foreach (ACOConnection aConnection in Connections)
                {
                    if (aConnection.FromNode.Equals(currentNode) &&
                        aConnection.ToNode.Equals(aAnt.StartNode))
                    {
                        aAnt.AddTravelledConnection(aConnection);
                        aAnt.AddAntTourLength(aConnection.Distance);
                        break;
                    }
                }

                Ants.Add(aAnt);
            }

            // Update pheromone levels
            foreach (ACOConnection aConnection in Connections)
            {
                float Sum = 0;
                foreach (ACOAnt TmpAnt in Ants)
                {
                    foreach (ACOConnection tmpConnection in TmpAnt.AntTravelledConnections)
                    {
                        if (aConnection.Equals(tmpConnection))
                        {
                            Sum += Q / Mathf.Max(TmpAnt.AntTourLength, 0.001f);
                        }
                    }
                }

                // Pheromone update formula
                float NewPheromoneLevel = (1 - EvaporationFactor) * aConnection.PheromoneLevel + Sum;
                aConnection.PheromoneLevel = Mathf.Max(NewPheromoneLevel, 0.001f);
                aConnection.PathProbability = 0;
            }
        }

        // Generate and return the route
        MyRoute = GenerateRoute(StartNode, MaxPathLength, Connections);
        return MyRoute;
    }

    /// <summary>
    /// Get all connections from a node.
    /// </summary>
    public List<ACOConnection> AllConnectionsFromNode(GameObject FromNode, List<ACOConnection> Connections)
    {
        List<ACOConnection> ConnectionsFromNode = new List<ACOConnection>();
        foreach (ACOConnection aConnection in Connections)
        {
            if (aConnection.FromNode == FromNode)
            {
                ConnectionsFromNode.Add(aConnection);
            }
        }
        return ConnectionsFromNode;
    }

    /// <summary>
    /// Get all connections from a node that haven't been visited.
    /// </summary>
    private List<ACOConnection> AllConnectionsFromNodeAndNotVisited(
        GameObject FromNode, List<ACOConnection> Connections, List<GameObject> VisitedList)
    {
        List<ACOConnection> ConnectionsFromNode = new List<ACOConnection>();
        foreach (ACOConnection aConnection in Connections)
        {
            if (aConnection.FromNode == FromNode)
            {
                if (!VisitedList.Contains(aConnection.ToNode))
                {
                    ConnectionsFromNode.Add(aConnection);
                }
            }
        }
        return ConnectionsFromNode;
    }

    /// <summary>
    /// Calculate total pheromone and visibility for probability calculation.
    /// </summary>
    private float CalculateTotalPheromoneAndVisibility(List<ACOConnection> ConnectionsFromNodeAndNotVisited)
    {
        float TotalPheromoneAndVisibility = 0;
        foreach (ACOConnection aConnection in ConnectionsFromNodeAndNotVisited)
        {
            TotalPheromoneAndVisibility +=
                (Mathf.Pow(aConnection.PheromoneLevel, Alpha) *
                 Mathf.Pow((1 / aConnection.Distance), Beta));
        }
        return TotalPheromoneAndVisibility;
    }

    /// <summary>
    /// Generate route by following highest pheromone connections.
    /// </summary>
    public List<ACOConnection> GenerateRoute(GameObject StartNode, int MaxPath, List<ACOConnection> Connections)
    {
        GameObject CurrentNode = StartNode;
        List<ACOConnection> Route = new List<ACOConnection>();
        List<GameObject> visited = new List<GameObject>();
        int PathCount = 0;

        while (CurrentNode != null && PathCount < MaxPath)
        {
            visited.Add(CurrentNode);
            List<ACOConnection> AllFromConnections = AllConnectionsFromNode(CurrentNode, Connections);

            if (AllFromConnections.Count > 0)
            {
                // Find highest pheromone connection that hasn't been visited
                ACOConnection HighestPheromoneConnection = null;
                float highestPheromone = -1f;

                foreach (ACOConnection aConnection in AllFromConnections)
                {
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
                    if (CurrentNode.Equals(StartNode))
                    {
                        break;
                    }
                }
                else
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
        GameObject CurrentNode = StartNode;
        List<ACOConnection> Route = new List<ACOConnection>();
        List<GameObject> visited = new List<GameObject>();
        int PathCount = 0;

        while (CurrentNode != null && !CurrentNode.Equals(GoalNode) && PathCount < MaxPath)
        {
            visited.Add(CurrentNode);
            List<ACOConnection> AllFromConnections = AllConnectionsFromNode(CurrentNode, Connections);

            if (AllFromConnections.Count > 0)
            {
                ACOConnection BestConnection = null;
                float bestScore = -1f;

                foreach (ACOConnection aConnection in AllFromConnections)
                {
                    if (!visited.Contains(aConnection.ToNode))
                    {
                        // Score based on pheromone and distance to goal
                        float distToGoal = Vector3.Distance(aConnection.ToNode.transform.position, 
                            GoalNode.transform.position);
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
            }
            else
            {
                break;
            }

            PathCount++;
        }

        return Route;
    }
}
