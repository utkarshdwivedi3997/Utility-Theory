using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GroundGrid : MonoBehaviour {
    [SerializeField]
    private float size = 1f;        // Size of each unit box in the grid

    public static GroundGrid Instance { get; set; }        // Instance of this grid

    public void Start()
    {
        // Singleton instance
        if (Instance==null)
        {
            Instance = this;
        }
        else if (Instance!=this)
        {
            Destroy(this);
        }
    }

    /// <summary>
    /// Get a Vector3 point closest to the passed Vector 3
    /// </summary>
    /// <param name="position">Vector3 position for finding closest grid point to</param>
    /// <returns>Vector3 - a point closest to position</returns>
    public Vector3 GetNearestPointOnGrid(Vector3 position)
    {
        position -= transform.position;

        float x = Mathf.RoundToInt(position.x / size);
        float y = position.y;
        float z = Mathf.RoundToInt(position.z / size);

        Vector3 gridPoint = new Vector3(x * size, y, z * size);

        gridPoint += transform.position;

        return gridPoint;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;

        Vector3 topLeft = transform.TransformPoint(-transform.localScale.x/2, 0, -transform.localScale.z/2);
        Vector3 currPos = topLeft;

        /*while (currPos.x < transform.localScale.x)
        {
            Vector3 point = Vector3.zero;
            while (currPos.z < transform.localScale.y)
            {
                currPos = new Vector3(currPos.x, currPos.y, currPos.z + size);

                point = GetNearestPointOnGrid(new Vector3(currPos.x, transform.position.y, currPos.z));
                Gizmos.DrawSphere(point, 0.1f);
            }
            Debug.Log(point);
            currPos = new Vector3(currPos.x + size, currPos.y, currPos.z);
        }*/
        for (float x = topLeft.x; x < 40; x += size)
        {
            for (float z = topLeft.z; z < 40; z += size)
            {
                Vector3 point = GetNearestPointOnGrid(new Vector3(x, transform.position.y, z));
                Gizmos.DrawWireSphere(point, 0.1f);
            }
        }
    }

    /* --------------- Getters and Setters Follow --------------- */
    public float GetCellSize()
    {
        return size;
    }

    public List<Vector3> GetPointsInCube(float startX, float startZ, float endX, float endZ)
    {
        List<Vector3> arr = new List<Vector3>();

        for (float x = startX; x <= endX; x += size)
        {
            for (float z = startZ; z <= endZ; z += size)
            {
                arr.Add(GetNearestPointOnGrid(new Vector3(x, transform.position.y, z)));
            }
        }

        return arr;
    }
}
