using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using System;

public class EnemyController_noUI : MonoBehaviour
{
    public bool isEnemyA;                                               // Enemy clan: A or B
    private string suffix;                                              // "_A" for clan A, "_B" for clan B

    private Rigidbody myRb;                                             // Rigidbody component attached to this enemy
    private NavMeshAgent myNav;                                         // NavMeshAgent component attached to this enemy

    private enum STATE { IDLE, HEAL, ENGAGE, COVER, RUN, HIDE };        // AI states
    private STATE currState;                                            // The current AI state
    public float moveSpeed;                                             // Speed of movement

    // Enemy attributes

    //public float lookForEnemyDistance = 100f;
    public float shootTime = 0.53f;                                     // Wait time before firing another bullet  
    private float timeToShoot = 0f;                                     // Time count for regular shooting
    public GameObject bulletPrefab;                                     // Prefab bullet GameObject
    public Transform muzzlePoint;                                       // Spawn point for bullets
    private GameObject currEnemyTarget;                                 // The current target to engage
    public LayerMask whatIsEnemy;                                       // Which layers to search for to find enemies

    // Cover attributes

    public float lookForCoverDistance = 20f;                            // Radius of cover search
    private GameObject closestCover = null;                             // The closest cover gameobject - starts with null
    private Vector3 closestCoverPos;
    public LayerMask whatIsCover;                                       // Which layers to search for to find cover
    private int coverStatus = 1;                                        // 0 = in cover, 1 = not in cover, looking for one
    Dictionary<Vector3, float> coverRiskLevels = new Dictionary<Vector3, float>();        // float - risk level of the current cover position being tested, GameObject - the actual position that is being tested
    public Camera myCam;
    private Plane[] planes;

    private bool isPaused = false;                                      // The game isn't paused at the start
    public Image healthBar;                                             // Health UI
    public GameObject coverValueCanvas;                                     // Panel to handle cover value display

    // Different values denoting some specific status of this enemy
    public float health, nEnemies, distanceToCover, maxAnxiousDistance, distanceToClosestEnemy, exhaustion;

    private float anxiety, awareness, coverNeed, coverPossibility;      // values needed to calculate priorities
    private float p_heal, p_cover, p_engage, p_run, p_hide;             // p_ denotes priority float values. All values lie between 0-1.

    // Use this for initialization
    void Start()
    {

        myRb = GetComponent<Rigidbody>();
        myNav = GetComponent<NavMeshAgent>();
        //myNav.stoppingDistance = 2f;

        // Which enemy clan is this current enemy a part of
        if (isEnemyA)
        {
            suffix = "_B";
        }
        else
        {
            suffix = "_A";
        }

        // Calculate priorities every 1s. We don't need to do this every frame using Update().
        InvokeRepeating("CalculatePriorities", 0f, 1f);
       
        currState = STATE.IDLE;         // Start at idle state
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        //CalculatePriorities();

        UpdateState();

        if (Input.GetButtonDown("Pause"))
        {
            isPaused = !isPaused;
        }
    }

    private void UpdateState()
    {
        switch (currState)
        {
            case STATE.IDLE: // do nothing
                break;
            case STATE.HEAL:    // simply heal self
                HealSelf();
                break;
            case STATE.ENGAGE:
                // shoot every "shootTime" seconds
                if (timeToShoot < shootTime)
                {
                    timeToShoot += Time.deltaTime;
                }
                if (timeToShoot >= shootTime)
                {
                    ShootAtEnemy();
                    timeToShoot = 0f;
                }
                
                //engage the enemy
                myNav.isStopped = false;
                EngageEnemy();
                break;
            case STATE.COVER:
                //if (closestCover != null)
                {
                    if (closestCoverPos!=null)
                    {
                        myNav.destination = closestCoverPos;
                        //Debug.DrawLine(myNav.transform.position, closestCoverPos, Color.cyan, 1f);
                    }
                    //myNav.destination = closestCover.transform.position;
                }
                myNav.isStopped = false;
                GetToCover();
                break;
        }
    }
    private void CalculatePriorities()
    {
        // Update all values necessary for priority calculation
        planes = GeometryUtility.CalculateFrustumPlanes(myCam);     // calculate camera frustrum planes every frame

        UpdateDistanceToClosestEnemy();
        closestCoverPos = FindCover();
        Debug.Log(closestCoverPos);
        // Calculate priorities
        p_heal = 1 / (1 + Mathf.Exp(0.2f * (health - 30f)));                                                            //Priority to heal      -- S curve (Logistic function)
        p_engage = Mathf.Pow(maxAnxiousDistance - distanceToClosestEnemy, 3f) / Mathf.Pow(maxAnxiousDistance, 3f);      //Exponential function

        coverNeed = 1 - Mathf.Pow(100 - (p_heal * p_engage) * 100, 3f) / Mathf.Pow(100, 3f);                            //Need to take cover. Depends on priority to heal and engage
        coverPossibility = 1 / (1 + Mathf.Exp(0.11f * (distanceToCover - 60f)));                                        //Is taking cover even possible?
        p_cover = coverNeed * coverPossibility * coverStatus;

        anxiety = (p_engage + (Mathf.Pow(nEnemies, 3f) / Mathf.Pow(100, 3f)) * 2f) / 3f;                                //Anxieity curve - both number of enemies and distance to enemies taken into account -- Exponential function - change power number to change steepness of curve
        awareness = 1 / (1 + Mathf.Exp(0.11f * (exhaustion - 50f)));                                                    //Depends on fatique/exhaustion level

        // NOT A GOOD WAY TO HANDLE THIS. Improve this later.
        float[] arr = { p_heal, p_engage, p_cover };
        float maxVal = Mathf.Max(arr);

        float THRESHOLD = 0.0035f;          // Float calculations are messy

        // Switch states depending on the highest priority
        if (maxVal > THRESHOLD)
        {
            switch (Array.IndexOf(arr, maxVal))
            {
                case 0:
                    currState = STATE.HEAL;
                    break;
                case 1:
                    currState = STATE.ENGAGE;
                    break;
                case 2:
                    currState = STATE.COVER;
                    break;
            }
        }
        else
        {
            currState = STATE.IDLE;
        }

        Debug.Log(currState);
    }

    /// <summary>
    /// Finds the closest cover and the distance to it.
    /// </summary>
    private Vector3 FindCover()
    {
        List<GameObject> c = new List<GameObject>();
        coverRiskLevels = new Dictionary<Vector3, float>();

        float minDistance = lookForCoverDistance + 1000;
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, lookForCoverDistance, whatIsCover);
        if (hitColliders.Length > 0)
        {
            foreach (Collider hit in hitColliders)
            {
                if (Vector3.Distance(hit.transform.position, transform.position) < minDistance)
                {
                    minDistance = Vector3.Distance(hit.transform.position, transform.position);
                    closestCover = hit.gameObject;
                    distanceToCover = minDistance;

                    //BoxCollider col = closestCover.GetComponent<BoxCollider>();
                    //col.size = col.size * 2;
                    Vector3 halfExtents = closestCover.transform.localScale;//GetComponent<Collider>().bounds.extents;
                    Debug.Log(halfExtents);
                    Vector3 startVec = new Vector3(closestCover.transform.position.x - halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z - halfExtents.z);
                    Vector3 endVec = new Vector3(closestCover.transform.position.x + halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z + halfExtents.z);

                    //Vector3 vec2 = new Vector3(closestCover.transform.position.x - halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z + halfExtents.z);
                    //Vector3 vec3 = new Vector3(closestCover.transform.position.x + halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z - halfExtents.z);

                    //Debug.DrawLine(startVec, vec2, Color.red, 0.5f);
                    //Debug.DrawLine(vec2, vec3, Color.blue, 0.5f);
                    //Debug.DrawLine(endVec, vec2, Color.green, 0.5f);
                    //Debug.DrawLine(endVec, vec3, Color.black, 0.5f);
                    //Debug.DrawLine(closestCover.transform.position, endVec, Color.red, 0.5f);

                    // Check to see if enemies are near this cover

                    Collider[] cols = Physics.OverlapBox(closestCover.transform.position, halfExtents, closestCover.transform.rotation, whatIsEnemy);
                    List<Collider> enemyCols = new List<Collider>();

                    foreach (Collider col in cols)                      // Get all enemy hits
                    {
                        if (col.gameObject.tag.Equals("Enemy" + suffix))
                        {
                            enemyCols.Add(col);
                        }
                    }

                    if (enemyCols.Count > 0)           // Make a box a little bigger than the size of the cover and check if enemy is in it
                    {
                        List<Vector3> possibleCoverAreas = GroundGrid.Instance.GetPointsInCube(startVec.x,startVec.z,endVec.x,endVec.z);

                        foreach (Vector3 area in possibleCoverAreas)
                        {
                            // If this area has not been checked yet
                            //if (coverRiskLevels.ContainsKey(area) == false)
                            {
                                float priorityForCover = 0;
                                //Debug.DrawLine(transform.position, area, Color.green, 0.5f);
                                // If this area is within the mesh itself
                                if (closestCover.GetComponent<Collider>().bounds.Contains(area))
                                {
                                    coverRiskLevels.Add(area, priorityForCover);
                                    continue;
                                }
                                else
                                {
                                    Debug.DrawLine(transform.position, area, Color.green, 0.5f);
                                    //myCam.transform.position = new Vector3(area.x, myCam.transform.position.y, area.z);
                                    //myCam.transform.LookAt(closestCover.transform.position);

                                    float enemyNum = 0;
                                    foreach (Collider enemy in enemyCols)
                                    {
                                        if (GeometryUtility.TestPlanesAABB(planes, enemy.bounds))
                                        {
                                            if (IsEnemyInSight(enemy))
                                            {
                                                if (suffix!="_A")
                                                Debug.Log("enemy seen");
                                                enemyNum++;
                                            }
                                            else
                                            {
                                                if (suffix!="_A")
                                                Debug.Log("clear!!");
                                            }
                                        }
                                        else
                                        {

                                        }
                                    }

                                    priorityForCover = Mathf.Pow((10 - enemyNum), 3) / Mathf.Pow(10, 3);
                                    coverRiskLevels.Add(area, priorityForCover);
                                }
                            }
                        }
                        //distanceToCover = 0;
                        //closestCover = null;
                        //minDistance = lookForCoverDistance + 1000;

                        //c.Add(Instantiate(coverValueCanvas, hit.transform.position + Vector3.up * 4f, Quaternion.identity));
                    }
                }
            }
        }

        // Return cover with highest priority
        if (coverRiskLevels.Count > 0)
        {
            float highestCoverValue = 0;
            foreach (KeyValuePair<Vector3, float> kvp in coverRiskLevels)
            {
                if (kvp.Value > highestCoverValue)
                {
                    highestCoverValue = kvp.Value;
                }
            }

            foreach (KeyValuePair<Vector3, float> kvp in coverRiskLevels)
            {
                if (kvp.Value >= highestCoverValue)
                {
                    //closestCover = kvp.Key;
                    Debug.Log("actual pos");
                    return kvp.Key;
                }
            }
        }
        return closestCover.transform.position;
    }

    private bool IsEnemyInSight(Collider enemy)
    {
        RaycastHit hit;
        Debug.DrawRay(myCam.transform.position, transform.forward * 10, Color.blue);
        if (Physics.Raycast(myCam.transform.position, transform.forward, out hit, 10))
        {
            if (hit.collider == enemy)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the closest enemy and the distance to it.
    /// </summary>
    private void UpdateDistanceToClosestEnemy()
    {
        distanceToClosestEnemy = maxAnxiousDistance * 2f;
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, maxAnxiousDistance, whatIsEnemy);
        if (hitColliders.Length > 0)
        {
            foreach (Collider hit in hitColliders)
            {
                if (Vector3.Distance(hit.transform.position, transform.position) < distanceToClosestEnemy)
                {
                    distanceToClosestEnemy = Vector3.Distance(hit.transform.position, transform.position);
                    currEnemyTarget = hit.gameObject;
                }
            }
        }
        nEnemies = hitColliders.Length;
    }

    /* ----------------- State Machine Functions ----------------- */

    private void HealSelf()
    {
        health += 15 * Time.deltaTime;
        healthBar.rectTransform.sizeDelta = new Vector2(health, 100);
        health = Mathf.Clamp(health, 0, 100);
    }

    /// <summary>
    /// Function to go to closest cover. Need to implement angle functionality so the AI knows which side of the cover to go to.
    /// </summary>
    private void GetToCover()
    {
        if (myNav.remainingDistance <= 0.2f)
        {
            myNav.isStopped = true;
            closestCover = null;
            coverStatus = 0;
            StartCoroutine(ResetCoverStatus());
        }
        else
        {
            Debug.DrawLine(myNav.transform.position, myNav.destination, Color.cyan);
            myNav.isStopped = false;
        }
    }

    /// <summary>
    /// Resets cover status so that AI starts looking for cover again
    /// </summary>
    IEnumerator ResetCoverStatus()
    {
        yield return new WaitForSeconds(3);
        coverStatus = 1;
    }

    /// <summary>
    /// Function to shoot at the current enemy target. Currently, only shoots in the forward direction.
    /// </summary>
    private void ShootAtEnemy()
    {
        GameObject currBullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        currBullet.GetComponent<Rigidbody>().AddForce(muzzlePoint.forward * 500f);
    }

    /// <summary>
    /// Function to chase the current enemy target. Uses NavMesh.
    /// </summary>
    private void EngageEnemy()
    {
        myNav.destination = currEnemyTarget.transform.position;
        Vector3 targetDir = new Vector3(currEnemyTarget.transform.position.x,transform.position.y,currEnemyTarget.transform.position.z) - transform.position;
        Quaternion rotateDir = Quaternion.LookRotation(targetDir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotateDir, 2f * Time.deltaTime);
        if (Vector3.Distance(transform.position, currEnemyTarget.transform.position) <= 0.8f)
        {
            myNav.isStopped = true;
        }
        else
        {
            myNav.isStopped = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // if hit by bullet (friendly or not), take damage and check if health is below 0
        if (other.gameObject.tag.Equals("Bullet"))
        {
            Destroy(other.gameObject);
            health -= 3;
            healthBar.rectTransform.sizeDelta = new Vector2(health, 100);
            if (health <= 0)
                Destroy(gameObject);
        }
    }

    /* ------------------- Colliders and Bounding box debug wireframes ------------------- */
    void DrawDebugBox(Vector3 extents, Transform center)
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(closestCover.transform.position, closestCover.GetComponent<Collider>().bounds.size * 2f);
    }
}
 