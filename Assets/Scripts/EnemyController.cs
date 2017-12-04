using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using System;

public class EnemyController : MonoBehaviour {

    // Enemy type
    public bool isEnemyA;
    private string suffix;

    private Rigidbody myRb;
    private NavMeshAgent myNav;
    private enum STATE { IDLE, HEAL, ENGAGE, COVER, RUN, HIDE };
    private STATE currState;
    public float moveSpeed;

    // Enemy stuff

    //public float lookForEnemyDistance = 100f;
    public float shootTime = 0.53f;
    private float timeToShoot = 0f;
    public GameObject bulletPrefab;
    public Transform muzzlePoint;
    private GameObject currEnemyTarget;
    public LayerMask whatIsEnemy;
    
    // Cover stuff

    public float lookForCoverDistance = 20f;
    private GameObject closestCover = null;
    private Vector3 closestCoverPos;
    public LayerMask whatIsCover;
    private Transform coverLocation;
    private int coverStatus = 1;
    Dictionary<Vector3, float> coverRiskLevels = new Dictionary<Vector3, float>();        // float - risk level of the current cover position being tested, GameObject - the actual position that is being tested
    public Camera myCam;
    private Plane[] planes;

    //public Slider an, aw, he, co, en;
    public GameObject UIPanel;
    private bool isPaused = false;
    public Text ui_anxiety, ui_awareness, ui_heal, ui_cover, ui_engage, ui_health, ui_fatigue, ui_distance, ui_nEnemies;
    public float health, nEnemies, distanceToCover, maxAnxiousDistance, distanceToClosestEnemy, exhaustion;

    private float anxiety, awareness, coverNeed, coverPossibility;
    private float p_heal, p_cover, p_engage, p_run, p_hide;         //p_ denotes priority float values. All values lie between 0-1.

    Dictionary<Text, float> priorities = new Dictionary<Text, float>();

    // Use this for initialization
    void Start () {

        myRb = GetComponent<Rigidbody>();
        myNav = GetComponent<NavMeshAgent>();
        //myNav.stoppingDistance = 2f;
        
        if (isEnemyA)
        {
            suffix = "_B";
        }
        else
        {
            suffix = "_A";
        }

        InvokeRepeating("CalculatePriorities", 0f, 1f);
        priorities.Add(ui_anxiety, anxiety);
        priorities.Add(ui_awareness, awareness);
        priorities.Add(ui_heal, p_heal);
        priorities.Add(ui_engage, p_engage);
        priorities.Add(ui_cover, p_cover);
        UIPanel.SetActive(false);
        currState = STATE.IDLE;
    }
	
	// Update is called once per frame
	void FixedUpdate () {
        //CalculatePriorities();

        UpdateState();

        if (Input.GetButtonDown("Pause"))
        {
            isPaused = !isPaused;
            UIPanel.SetActive(isPaused);
        }

        ui_health.text = health.ToString("0.00");
        ui_fatigue.text = exhaustion.ToString("0.00");
        ui_distance.text = distanceToClosestEnemy.ToString("0.00");
        ui_nEnemies.text = nEnemies.ToString("0.00");
        ui_anxiety.text = "Anxiety: " + anxiety.ToString("0.00");
        ui_awareness.text = "Awareness: " + awareness.ToString("0.00");
        ui_heal.text = "p_heal: " + p_heal.ToString("0.00");
        ui_cover.text = "p_cover: " + p_cover.ToString("0.00");
        ui_engage.text = "p_engage: " + p_engage.ToString("0.00");

    }

    private void UpdateState()
    {
        switch (currState)
        {
            case STATE.IDLE: // do nothing
                break;
            case STATE.HEAL:
                HealSelf();
                break;
            case STATE.ENGAGE:
                if (timeToShoot < shootTime)
                {
                    timeToShoot += Time.deltaTime;
                }
                if (timeToShoot>=shootTime)
                {
                    ShootAtEnemy();
                    timeToShoot = 0f;
                }
                myNav.isStopped = false;
                EngageEnemy();
                break;
            case STATE.COVER:
                //if (closestCover != null)
                {
                    if (closestCoverPos != null)
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
        //Update all values necessary for priority calculation
        planes = GeometryUtility.CalculateFrustumPlanes(myCam);     // calculate camera frustrum planes every frame

        UpdateDistanceToClosestEnemy();
        if (currState != STATE.COVER)
        {
            FindCover();
            p_cover = 0;
        }

        p_heal = 1 / (1 + Mathf.Exp(0.2f * (health - 30f)));                                                            //Priority to heal      -- S curve (Logistic function)
        p_engage = Mathf.Pow(maxAnxiousDistance - distanceToClosestEnemy, 3f) / Mathf.Pow(maxAnxiousDistance, 3f);      //Exponential function

        if (currState != STATE.COVER)
        {
            coverNeed = 1 - Mathf.Pow(100 - (p_heal * p_engage) * 100, 3f) / Mathf.Pow(100, 3f);                            //Need to take cover. Depends on priority to heal and engage
            coverPossibility = 1 / (1 + Mathf.Exp(0.11f * (distanceToCover - 60f)));                                        //Is taking cover even possible?
            p_cover = coverNeed * coverPossibility * coverStatus;
        }
        anxiety = (p_engage + (Mathf.Pow(nEnemies, 3f) / Mathf.Pow(100, 3f)) * 2f) / 3f;                                //Anxieity curve - both number of enemies and distance to enemies taken into account -- Exponential function - change power number to change steepness of curve
        awareness = 1 / (1 + Mathf.Exp(0.11f * (exhaustion - 50f)));                                                    //Depends on fatique/exhaustion level

        float[] arr = { p_heal, p_engage, p_cover };
        float maxVal = Mathf.Max(arr);

        float THRESHOLD = 0.0035f;

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

                    /*
                    Vector3 vec2 = new Vector3(closestCover.transform.position.x - halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z + halfExtents.z);
                    Vector3 vec3 = new Vector3(closestCover.transform.position.x + halfExtents.x, closestCover.transform.position.y, closestCover.transform.position.z - halfExtents.z);

                    Debug.DrawLine(startVec, vec2, Color.red, 0.5f);
                    Debug.DrawLine(vec2, vec3, Color.blue, 0.5f);
                    Debug.DrawLine(endVec, vec2, Color.green, 0.5f);
                    Debug.DrawLine(endVec, vec3, Color.black, 0.5f);
                    Debug.DrawLine(closestCover.transform.position, endVec, Color.red, 0.5f);
                    */

                    // Check to see if enemies are near this cover

                    Collider[] cols = Physics.OverlapBox(closestCover.transform.position, halfExtents*500, closestCover.transform.rotation, whatIsEnemy);
                    List<Collider> enemyCols = new List<Collider>();
                    closestCoverPos = closestCover.transform.position;
                    foreach (Collider col in cols)                      // Get all enemy hits
                    {
                        //Debug.Log(col.gameObject);
                        if (col.gameObject.tag.Equals("Enemy" + suffix))
                        {
                            enemyCols.Add(col);
                        }
                    }

                    if (enemyCols.Count > 0)           // Make a box a little bigger than the size of the cover and check if enemy is in it
                    {
                        List<Vector3> possibleCoverAreas = GroundGrid.Instance.GetPointsInCube(startVec.x, startVec.z, endVec.x, endVec.z);

                        foreach (Vector3 area in possibleCoverAreas)
                        {
                            // If this area has not been checked yet
                            if (coverRiskLevels.ContainsKey(area) == false)
                            {
                                Debug.DrawLine(transform.position, area, Color.green, 0.5f);

                                float priorityForCover = 0;
                                //Debug.DrawLine(transform.position, area, Color.green, 0.5f);
                                // If this area is within the mesh itself
                                if (closestCover.GetComponent<Collider>().bounds.Contains(area))
                                {
                                    //coverRiskLevels.Add(area, priorityForCover);
                                    continue;
                                }
                                else
                                {
                                    

                                    float enemyNum = 0;
                                    foreach (Collider enemy in enemyCols)
                                    {
                                        myCam.transform.position = new Vector3(area.x, myCam.transform.position.y, area.z);
                                        myCam.transform.LookAt(enemy.transform.position);
                                        if (GeometryUtility.TestPlanesAABB(planes, enemy.bounds))
                                        {
                                            if (IsEnemyInSight(enemy))
                                            {
                                                if (suffix != "_A")
                                                    Debug.Log("enemy seen");
                                                enemyNum++;
                                            }
                                            else
                                            {
                                                if (suffix != "_A")
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
                    Debug.DrawLine(myNav.transform.position, kvp.Key);
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

    private void UpdateDistanceToClosestEnemy()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, maxAnxiousDistance, whatIsEnemy);
        if (hitColliders.Length > 0)
        {
            distanceToClosestEnemy = Vector3.Distance(transform.position, hitColliders[0].transform.position);
            currEnemyTarget = hitColliders[0].gameObject;
        }
        nEnemies = hitColliders.Length;
    }

    private void HealSelf()
    {
        health += 15 * Time.deltaTime;
        health = Mathf.Clamp(health, 0, 100);
    }

    private void GetToCover()
    {
        if (myNav.remainingDistance < 2f)
        {
            myNav.isStopped = true;
            coverStatus = 0;
            StartCoroutine(ResetCoverStatus());
        }
        else
        {
            myNav.isStopped = false;
        }

    }

    /// <summary>
    /// Resets cover status so that AI starts looking for cover again
    /// </summary>
    IEnumerator ResetCoverStatus()
    {
        yield return new WaitForSeconds(7);
        coverStatus = 1;
    }

    private void ShootAtEnemy()
    {
        GameObject currBullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        currBullet.GetComponent<Rigidbody>().AddForce(muzzlePoint.forward * 500f);
    }

    private void EngageEnemy()
    {
        myNav.destination = currEnemyTarget.transform.position;
        Vector3 targetDir = new Vector3(currEnemyTarget.transform.position.x, transform.position.y, currEnemyTarget.transform.position.z) - transform.position;
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
        if (other.gameObject.tag.Equals("Bullet"))
        {
            Destroy(other.gameObject);
            health -= 3;
        }
    }

    /* ---------------- Getters and Setters follow ---------------- */
    public void changeFatigue(float val)
    {
        exhaustion = val;
        CalculatePriorities();
    }

    public void changeHealth(float val)
    {
        health = val;
        CalculatePriorities();
    }

    public void changeNumEnemies(float val)
    {
        nEnemies = val;
        CalculatePriorities();
    }

    public void changeDistance(float val)
    {
        distanceToClosestEnemy = val;
        CalculatePriorities();
    }
}
