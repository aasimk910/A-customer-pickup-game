using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Ant class that tracks tour information.
/// Stores the total distance traveled and connections used.
/// </summary>
public class ACOAnt
{
    private float antTourLength = 0;
    public float AntTourLength
    {
        set { antTourLength = value; }
        get { return antTourLength; }
    }

    private List<ACOConnection> antTravelledConnections = new List<ACOConnection>();
    public List<ACOConnection> AntTravelledConnections
    {
        get { return antTravelledConnections; }
    }

    private GameObject startNode;
    public GameObject StartNode
    {
        set { startNode = value; }
        get { return startNode; }
    }

    public ACOAnt()
    {
    }

    public void AddAntTourLength(float tourLength)
    {
        this.AntTourLength += tourLength;
    }

    public void AddTravelledConnection(ACOConnection aConnection)
    {
        antTravelledConnections.Add(aConnection);
    }

    public void Reset()
    {
        antTourLength = 0;
        antTravelledConnections.Clear();
        startNode = null;
    }
}
