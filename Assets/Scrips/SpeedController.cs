using TMPro;
using UnityEngine;

public class SpeedController : MonoBehaviour
{
    public CubeMove cubemove;
    public TMP_Text speedText;
    public HoldableButton[] moveButtons; // 顺序: 0左 1右 2上 3下 4前 5后
    public float minSpeed = 5f;
    public float maxSpeed = 50f;

    void Start()
    {
        UpdateDisplay();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            IncreaseNumber();
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            DecreaseNumber();
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            ResetNumber();
        }

        float step = cubemove.moveSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.W)) cubemove.MoveForward(step);
        if (Input.GetKey(KeyCode.S)) cubemove.MoveBack(step);
        if (Input.GetKey(KeyCode.A)) cubemove.MoveLeft(step);
        if (Input.GetKey(KeyCode.D)) cubemove.MoveRight(step);
        if (Input.GetKey(KeyCode.Q)) cubemove.MoveUp(step);
        if (Input.GetKey(KeyCode.E)) cubemove.MoveDown(step);

        if (moveButtons == null)
            return;
        for (int i = 0; i < moveButtons.Length; i++)
        {
            if (moveButtons[i].IsHeld)
                MoveByIndex(i, step);
        }
    }

    void MoveByIndex(int buttonIndex, float step)
    {
        switch (buttonIndex)
        {
            case 0: cubemove.MoveLeft(step); break;
            case 1: cubemove.MoveRight(step); break;
            case 2: cubemove.MoveUp(step); break;
            case 3: cubemove.MoveDown(step); break;
            case 4: cubemove.MoveForward(step); break;
            case 5: cubemove.MoveBack(step); break;
        }
    }

    void IncreaseNumber()
    {
        cubemove.moveSpeed = Mathf.Clamp(cubemove.moveSpeed + 5f, minSpeed, maxSpeed);
        UpdateDisplay();
    }

    void DecreaseNumber()
    {
        cubemove.moveSpeed = Mathf.Clamp(cubemove.moveSpeed - 5f, minSpeed, maxSpeed);
        UpdateDisplay();
    }

    void ResetNumber()
    {
        cubemove.moveSpeed = 10f;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        speedText.text = cubemove.moveSpeed.ToString("F0");
    }
}
