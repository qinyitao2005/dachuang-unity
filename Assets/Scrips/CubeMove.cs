using UnityEngine;

public class CubeMove : MonoBehaviour
{
    public float moveSpeed = 10;
    public Transform aim;

    private void Start()
    {
        if (aim == null)
            aim = transform;
    }

    public void MoveRight(float movespeed)
    {
        aim.position += Vector3.right * movespeed * Time.deltaTime;
    }
    public void MoveLeft(float movespeed)
    {
        aim.position += Vector3.left * movespeed * Time.deltaTime;
    }
    public void MoveUp(float movespeed)
    {
        aim.position += Vector3.up * movespeed * Time.deltaTime;
    }
    public void MoveDown(float movespeed)
    {
        aim.position += Vector3.down * movespeed * Time.deltaTime;
    }
    public void MoveForward(float movespeed)
    {
        aim.position += Vector3.forward * movespeed * Time.deltaTime;
    }
    public void MoveBack(float movespeed)
    {
        aim.position += Vector3.back * movespeed * Time.deltaTime;
    }
}
