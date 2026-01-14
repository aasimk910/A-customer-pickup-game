using UnityEngine;

/// <summary>
/// ACO Connection between two waypoint nodes.
/// Tracks pheromone level and path probability.
/// </summary>
public class ACOConnection
{
    public float Distance { get; private set; }
    public float PheromoneLevel { get; set; }
    public float PathProbability { get; set; }
    public GameObject FromNode { get; private set; }
    public GameObject ToNode { get; private set; }

    public ACOConnection()
    {
    }

    public void SetConnection(GameObject fromNode, GameObject toNode, float defaultPheromoneLevel)
    {
        FromNode = fromNode;
        ToNode = toNode;
        Distance = Vector3.Distance(fromNode.transform.position, toNode.transform.position);
        PheromoneLevel = defaultPheromoneLevel;
        PathProbability = 0;
    }
}
