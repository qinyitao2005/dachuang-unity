using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private CharacterController characterController;
    public float moveSpeed;
    public float jumpSpeed;
    private float horizontalMove, verticalMove;

    private Vector3 direction;

    public bool canMove = true;
    private void Start()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        horizontalMove = Input.GetAxis("Horizontal")*moveSpeed;
        verticalMove = Input.GetAxis("Vertical")*moveSpeed;
        direction = transform.forward * verticalMove + transform.right * horizontalMove;
        characterController.Move(direction * Time.deltaTime);
    }


}
