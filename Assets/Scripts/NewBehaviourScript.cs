using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    Rigidbody2D rb2d;
    float horizontalInput;
    

    public float moveSpeed = 10f;
    public float jumpSpeed = 5f;

    public Transform GroundCheckPoint;
    public LayerMask GroundLayer;
    float GroundCheckRadius = 0.2f;

    Animator anim;
    public float gameOverHeight = -4f;

    // Start is called before the first frame update
    void Start()
    {
        rb2d = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        horizontalInput = Input.GetAxis("Horizontal");
        float nextVelocityX = horizontalInput * moveSpeed; 
        float nextVelocityY = rb2d.velocity.y;

        bool isGrounded = CheckGrounded();

        if(isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            nextVelocityY = jumpSpeed;
        }

        if(horizontalInput < 0){
            transform.localScale = new Vector3(-1, 1, 1);
        }
        else if(horizontalInput > 0){
            transform.localScale = new Vector3(1, 1, 1);
        }

         anim.SetFloat("XSpeed", Mathf.Abs(nextVelocityX));
        anim.SetFloat("YSpeed", nextVelocityY);
        anim.SetBool("Grounded", isGrounded);

        rb2d.velocity = new Vector2(nextVelocityX, nextVelocityY);

       

        if(transform.position.y < gameOverHeight){
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    
    }

    bool CheckGrounded(){
        return Physics2D.OverlapCircle(GroundCheckPoint.position, GroundCheckRadius, GroundLayer);
    }
}
