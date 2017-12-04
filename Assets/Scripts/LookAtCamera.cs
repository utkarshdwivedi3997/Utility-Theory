using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LookAtCamera : MonoBehaviour {

    public Camera myCam;
	// Use this for initialization
	void Start () {
		if (myCam==null)
        {
            myCam = Camera.main;
        }
	}
	
	// Update is called once per frame
	void Update () {
        transform.LookAt(transform.position + myCam.transform.rotation * Vector3.back, myCam.transform.rotation * Vector3.up);
	}
}
