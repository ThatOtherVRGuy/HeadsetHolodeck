using UnityEngine;
using TMPro;
using System;

public class LoadingStatusController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    public void SetStatus(string status, Single progress)
    {
        var textMesh = GetComponentInChildren<TMP_Text>();
        if (textMesh != null)
        {
            textMesh.text = $"{status}\n{progress * 100.0f:0.0}%";
        }
    }
}
