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
    public float moveSpeed;

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
    public LayerMask whatIsCover;                                       // Which layers to search for to find cover


    //public Slider an, aw, he, co, en;
    private bool isPaused = false;                                      // The game isn't paused at the start
    public Image healthBar;                                             // Health UI

    // Different values denoting some specific status of this enemy
    public float health, nEnemies, distanceToCover, maxAnxiousDistance, distanceToClosestEnemy, exhaustion;

    private float anxiety, awareness;
    private float p_heal, p_cover, p_engage, p_run, p_hide;         //p_ denotes priority float values. All values lie between 0-1.

    Dictionary<Text, float> priorities = new Dictionary<Text, float>();     // This is just to change values from the UI

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
    void Update()
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
                myNav.destination = closestCover.transform.position;
                myNav.isStopped = false;
                GetToCover();
                break;
        }
    }
    private void CalculatePriorities()
    {
        // Update all values necessary for priority calculation
        UpdateDistanceToClosestEnemy();
        FindCover();


        // Actually calculate the values
        p_heal = 1 / (1 + Mathf.Exp(0.2f * (health - 30f)));                                                            //Priority to heal      -- S curve (Logistic function)
        p_engage = Mathf.Pow(maxAnxiousDistance - distanceToClosestEnemy, 3f) / Mathf.Pow(maxAnxiousDistance, 3f);      //Exponential function
        p_cover = 1 - Mathf.Pow(100 - (p_heal * p_engage) * 100, 3f) / Mathf.Pow(100, 3f);                              //Try this? ((p_heal * 2.5f) + (p_engage * 2.5f)) / 4f;
        anxiety = (p_engage + (Mathf.Pow(nEnemies, 3f) / Mathf.Pow(100, 3f)) * 2f) / 3f;                                //Anxieity curve - both number of enemies and distance to enemies taken into account -- Exponential function - change power number to change steepness of curve
        awareness = 1 / (1 + Mathf.Exp(0.11f * (exhaustion - 50f)));                                                    //Depends on fatique/exhaustion level

        // NOT A GOOD WAY TO HANDLE THIS. Improve this later.
        float[] arr = { p_heal, p_engage, p_cover };
        float maxVal = Mathf.Max(arr);

        float THRESHOLD = 0.0035f;

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
    private void FindCover()
    {
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
                }
            }
        }
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
        if (Vector3.Distance(transform.position, closestCover.transform.position) <= 0.8f)
        {
            myNav.isStopped = true;
        }
        else
        {
            myNav.isStopped = false;
        }
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