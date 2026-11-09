using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class InputCode
{
    public string name;
    public bool anywhere;          // true = works at all times
    public CodeButton[] sequence;
    public UnityEvent onMatched;
}

public class CodeInputSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;
    [SerializeField] List<InputCode> codes;
    [SerializeField] float timeout = 1.5f; // max gap between presses before the sequence resets

    readonly List<CodeButton> buffer = new();
    float lastPressTime;
    int maxLength;

    int zoneCount; // how many code spots the player is currently inside

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnEnable()
    {
        maxLength = 0;
        foreach (var c in codes) maxLength = Mathf.Max(maxLength, c.sequence.Length);
        input.CodeButtonPressed += OnPressed;
    }

    void OnDisable() => input.CodeButtonPressed -= OnPressed;

    void OnPressed(CodeButton button)
    {
        if (Time.unscaledTime - lastPressTime > timeout) buffer.Clear();
        lastPressTime = Time.unscaledTime;

        buffer.Add(button);
        if (buffer.Count > maxLength) buffer.RemoveAt(0);

        foreach (var code in codes)
        {
            if (!code.anywhere && zoneCount <= 0) continue;
            if (!EndsWith(code.sequence)) continue;

            Debug.Log($"Code entered: {code.name}");
            code.onMatched?.Invoke();
            buffer.Clear();
            break;
        }
    }

    bool EndsWith(CodeButton[] sequence)
    {
        if (sequence.Length == 0 || buffer.Count < sequence.Length) return false;

        int offset = buffer.Count - sequence.Length;
        for (int i = 0; i < sequence.Length; i++)
            if (buffer[offset + i] != sequence[i]) return false;

        return true;
    }

    public void EnterZone() => zoneCount++;
    public void ExitZone() => zoneCount = Mathf.Max(0, zoneCount - 1);
}
