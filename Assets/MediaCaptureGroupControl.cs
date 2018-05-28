using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.WSA;

public class MediaCaptureGroupControl : MonoBehaviour {

    private MediaCaptureRawSensor[] rawSensors;

	// Use this for initialization
	void Start () {
        Application.targetFrameRate = 60;
        HolographicSettings.ReprojectionMode = HolographicSettings.HolographicReprojectionMode.Disabled;
        rawSensors = GetComponents<MediaCaptureRawSensor>();
    }
	
	// Update is called once per frame
	void Update () {
		
	}

    public void OnClick() {
        foreach (var r in rawSensors) {
            r.OnClick();
        }
    }

}
