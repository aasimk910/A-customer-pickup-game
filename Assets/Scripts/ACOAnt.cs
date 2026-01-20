using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ACO Ant class that tracks tour information.
/// Stores the total distance traveled and connections used.
/// </summary>
public class ACOAnt
{
    #region Properties
    
    public float AntTourLength { get; set; } = 0;

    private List<ACOConnection> antTravelledConnections = new List<ACOConnection>();
    public List<ACOConnection> AntTravelledConnections => antTravelledConnections;

    public GameObject StartNode { get; set; }
    
    #endregion

    #region Constructor
    
    public ACOAnt()
    {
    }
    
    #endregion

    #region Public Methods

    public void AddAntTourLength(float tourLength)
    {
        AntTourLength += tourLength;
    }

    public void AddTravelledConnection(ACOConnection aConnection)
    {
        antTravelledConnections.Add(aConnection);
    }

    public void Reset()
    {
        AntTourLength = 0;
        antTravelledConnections.Clear();
        StartNode = null;
    }
    
    #endregion
}
