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
    public LayerMask whatIsCover;


    //public Slider an, aw, he, co, en;
    public GameObject UIPanel;
    private bool isPaused = false;
    public Text ui_anxiety, ui_awareness, ui_heal, ui_cover, ui_engage, ui_health, ui_fatigue, ui_distance, ui_nEnemies;
    public float health, nEnemies, distanceToCover, maxAnxiousDistance, distanceToClosestEnemy, exhaustion;

    private float anxiety, awareness;
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
	void Update () {
        //CalculatePriorities();

        UpdateState();

        if (Input.GetButtonDown("Pause"))
        {
            isPaused = !isPaused;
            UIPanel.SetActive(isPaused);
        }

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
                myNav.destination = closestCover.transform.position;
                myNav.isStopped = false;
                GetToCover();
                break;
        }
    }
    private void CalculatePriorities()
    {
        //Update all values necessary for priority calculation
        UpdateDistanceToClosestEnemy();
        FindCover();

        p_heal = 1 / (1 + Mathf.Exp(0.2f * (health - 30f)));                                                            //Priority to heal      -- S curve (Logistic function)
        p_engage = Mathf.Pow(maxAnxiousDistance - distanceToClosestEnemy, 3f) / Mathf.Pow(maxAnxiousDistance, 3f);      //Exponential function
        p_cover = 1 - Mathf.Pow(100 - (p_heal * p_engage) * 100, 3f) / Mathf.Pow(100, 3f);                              //Try this? ((p_heal * 2.5f) + (p_engage * 2.5f)) / 4f;
        anxiety = (p_engage + (Mathf.Pow(nEnemies, 3f) / Mathf.Pow(100, 3f)) * 2f) / 3f;                                //Anxieity curve - both number of enemies and distance to enemies taken into account -- Exponential function - change power number to change steepness of curve
        awareness = 1 / (1 + Mathf.Exp(0.11f * (exhaustion - 50f)));                                                    //Depends on fatique/exhaustion level

        ui_health.text = health.ToString("0.00");
        ui_fatigue.text = exhaustion.ToString("0.00");
        ui_distance.text = distanceToClosestEnemy.ToString("0.00");
        ui_nEnemies.text = nEnemies.ToString("0.00");
        ui_anxiety.text = "Anxiety: " + anxiety.ToString ("0.00");
        ui_awareness.text = "Awareness: " + awareness.ToString("0.00");
        ui_heal.text = "p_heal: " + p_heal.ToString("0.00");
        ui_cover.text = "p_cover: " + p_cover.ToString("0.00");
        ui_engage.text = "p_engage: " + p_engage.ToString("0.00");

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
        if (Vector3.Distance(transform.position, closestCover.transform.position) <= 0.8f)
        {
            myNav.isStopped = true;
        }
        else
        {
            myNav.isStopped = false;
        }
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
