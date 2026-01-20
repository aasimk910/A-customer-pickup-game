using UnityEngine;

/// <summary>
/// Connection between two waypoint nodes for A* pathfinding.
/// Cost is lazily calculated as Euclidean distance.
/// </summary>
public class Connection
{
    #region Private Fields
    
    private float cost = 0;
    private GameObject fromNode;
    private GameObject toNode;
    
    #endregion

    #region Properties

    public float Cost
    {
        get
        {
            if (cost == 0 && fromNode != null && toNode != null)
            {
                cost = Vector3.Distance(fromNode.transform.position, toNode.transform.position);
            }
            return cost;
        }
        set { cost = value; }
    }

    public GameObject FromNode
    {
        get { return fromNode; }
        set
        {
            fromNode = value;
            cost = 0; // Reset cost for recalculation
        }
    }

    public GameObject ToNode
    {
        get { return toNode; }
        set
        {
            toNode = value;
            cost = 0; // Reset cost for recalculation
        }
    }
    
    #endregion

    #region Constructor

    public Connection()
    {
    }
    
    #endregion
}