
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Fake_Battle_Animation_Switcher : MonoBehaviour
{
    public Animator EnemyAnimator;
    [SerializeField] private Fake_Battle_Animation_Switcher Enemy_Switch;
    private bool Can_Attack = true;
    private bool Block = false;
    private bool Dodje = false;
    private bool BlockDirection = true;
    [SerializeField] private float Max_Evade;
    [SerializeField] private float Max_Block;
    [SerializeField] private int MaxCombo;
    [SerializeField] private int MaxJab;
    [SerializeField] private int MaxUlt;
    [SerializeField] private string[] Animation_Play_Combo;
    [SerializeField] private string[] Animation_Play_jab;
    [SerializeField] private AudioSource HitSound;
    [SerializeField] private AudioSource WhooshSound;
    [SerializeField] private AudioSource DodjeSound;
    [SerializeField] private AudioSource BlockSound;


    //public void Switch_Attack_Animation_Combo_Random(string[] Animation_Play, int Max)
    //{
    //    if (0 == Random.Range(0, Max) && (Enemy_Switch.Can_Attack = false))
    //    {
    //        EnemyAnimator.SetTrigger(Animation_Play[Random.Range(0, Animation_Play.Length)]);
    //    }
    //}
    public void Play_Sound(string Sound)
    {
        switch (Sound)
        {
            case "Dodje":
                DodjeSound.pitch = Random.Range(0.8f, 1.2f);
                DodjeSound.Play();
                break;
            case "Hit":
                HitSound.pitch = Random.Range(0.8f, 1.2f);
                HitSound.Play(); 
                break;
            case "Whoosh":
                WhooshSound.pitch = Random.Range(0.8f, 1.2f);
                WhooshSound.Play();
                break;
            case "Block":
                BlockSound.pitch = Random.Range(0.8f, 1.2f);
                BlockSound.Play();
                break;
        }
    }
    
    public void Switch_To_Attack()
    {
        if (0 == Random.Range(0, MaxCombo) && (Enemy_Switch.Can_Attack = true))
        {
            ResetTriggers();
            EnemyAnimator.SetTrigger(Animation_Play_Combo[Random.Range(0,Animation_Play_Combo.Length)]);
        }
        else if (0 == Random.Range(0, MaxJab) && (Enemy_Switch.Can_Attack = true))
        {
            ResetTriggers();
            EnemyAnimator.SetTrigger(Animation_Play_jab[Random.Range(0,Animation_Play_jab.Length)]);
            EnemyAnimator.SetFloat("L_R_Jabs", Random.Range(0,1));
            MaxJab++;
        }
        else if (0 == Random.Range(0, MaxUlt) && (Enemy_Switch.Can_Attack = true))
        {
            ResetTriggers();
            EnemyAnimator.SetTrigger("Ult");
        }
    }
    public void ResetTriggers()
    {
        EnemyAnimator.ResetTrigger("Combo_L");
        EnemyAnimator.ResetTrigger("Combo_R");
        EnemyAnimator.ResetTrigger("Combo_U");
        EnemyAnimator.ResetTrigger("Hit_Combo_L");
        EnemyAnimator.ResetTrigger("Hit_Combo_R");
        EnemyAnimator.ResetTrigger("Hit_Combo_U");
        EnemyAnimator.ResetTrigger("Block_OFF");
        EnemyAnimator.ResetTrigger("Block_U");
        EnemyAnimator.ResetTrigger("Block_D");
        EnemyAnimator.ResetTrigger("Block_Hit");
        EnemyAnimator.ResetTrigger("Dodje");
        EnemyAnimator.ResetTrigger("Ult");
        EnemyAnimator.ResetTrigger("Ult_Hit");
        EnemyAnimator.ResetTrigger("Jab_U");
        EnemyAnimator.ResetTrigger("Hit_Jab_U");
        EnemyAnimator.ResetTrigger("Jab_D");
        EnemyAnimator.ResetTrigger("Hit_Jab_D");
        EnemyAnimator.ResetTrigger("Switch_Block");
    }

    //public void Switch_Attack(List<string> Animation_Play_Combo, List<string> Animation_Play_jab)
    //{
    //    if (0 == Random.Range(0, MaxCombo) && (Enemy_Switch.Can_Attack = true))
    //    {
    //        EnemyAnimator.SetTrigger(Animation_Play_Combo[Random.Range(0, Animation_Play_Combo.Count)]);
    //    }
    //    else if (0 == Random.Range(0, MaxJab) && (Enemy_Switch.Can_Attack = true))
    //    {
    //        EnemyAnimator.SetTrigger(Animation_Play_jab[Random.Range(0, Animation_Play_jab.Count)]);
    //        EnemyAnimator.SetFloat("L_R_Jabs", Random.Range(0, 1));
    //    }
    //    else if (0 == Random.Range(0, MaxUlt) && (Enemy_Switch.Can_Attack = true))
    //    {
    //        EnemyAnimator.SetTrigger("Ult");
    //    }
    //}
    public void EnemyCanAttack_Off()
    {
        Enemy_Switch.Can_Attack = false;
    }
    public void EnemyCanAttack_On()
    {
        Enemy_Switch.Can_Attack = true;
    }

    public void SwitchToIdle()
    {
        EnemyAnimator.SetTrigger("Set_Idle");
    }
    public void EnemyBlockOrEvade_Off()
    {
        if (Enemy_Switch.Block == true )
        {
            EnemyAnimator.SetTrigger("Block_OFF");
        }
        Enemy_Switch.Block = false;
        Enemy_Switch.Dodje = false;
    }

    public void Try_Get_Block_or_Evade()
    {
        if (0 == Random.Range(0, Max_Block))
        {
            Enemy_Switch.Block = true;
        }
        else if (0 == Random.Range(0, Max_Evade))
        {
            Enemy_Switch.Dodje = true;
            Max_Evade++;
            Max_Evade++;
        }
    }

    public void Try_Get_Evade()
    {
        if (0 == Random.Range(0, Max_Evade))
        {
            Enemy_Switch.Dodje = true;
            Max_Evade++;
            Max_Evade++;
        }
    }

    public void Switch_To_Hit(string Animation_Hit_Play)
    {
        if (Enemy_Switch.Block == true || Enemy_Switch.Dodje == true)
        {

        }
        else
        {
            EnemyAnimator.SetTrigger(Animation_Hit_Play);
            if (!(Max_Block<= 0))
            {
                Max_Block--;
            }
            if (!(Max_Evade<= 0))
            {
                Max_Evade--;
            }              
        }
        if (MaxJab>= 6)
        {
            MaxJab--;
        }
    }
    public void Switch_To_HitUlt()
    {
        EnemyBlockOrEvade_Off();
        ResetTriggers();
        EnemyAnimator.SetTrigger("Ult_Hit");
    }

    public void DirectionBlock_Attack(string Direction)
    {
        if (Direction=="true")
        {
            if (Enemy_Switch.BlockDirection == true)
            {

            }
            else if (Enemy_Switch.BlockDirection == false)
            {
                Enemy_Switch.BlockDirection = true;
                EnemyAnimator.SetTrigger("Switch_Block");
            }
        }else if (Direction=="false") 
        {
            if (Enemy_Switch.BlockDirection == false)
            {

            }
            else if (Enemy_Switch.BlockDirection == true)
            {
                Enemy_Switch.BlockDirection = false;
                EnemyAnimator.SetTrigger("Switch_Block");
            }
        }
    }

    public void SwitchToBlock(string Direction)
    {
        if (Enemy_Switch.Block == true)
        {
            if (Direction == "true")
            {
                if(Enemy_Switch.BlockDirection == true)
                {
                    ResetTriggers();
                    EnemyAnimator.SetTrigger("Block_U");
                    Max_Block++;
                    Max_Block++;
                }
                else if (Enemy_Switch.BlockDirection == false)
                {
                    ResetTriggers();
                    Enemy_Switch.BlockDirection=true;
                    EnemyAnimator.SetTrigger("Block_U");
                    Max_Block++;
                    Max_Block++;
                }
            }else if(Direction == "false")
            {
                if (Enemy_Switch.BlockDirection == false)
                {
                    ResetTriggers();
                    EnemyAnimator.SetTrigger("Block_D");
                }
                else if (Enemy_Switch.BlockDirection == true)
                {
                    ResetTriggers();
                    Enemy_Switch.BlockDirection = false;
                    EnemyAnimator.SetTrigger("Block_D");
                }
            }
        }
    }
    public void Switch_To_Hit_Block( )
    {
        if (Enemy_Switch.Block == true)
        {
            ResetTriggers();
            EnemyAnimator.SetTrigger("Block_Hit");
        }
    }
    public void Switch_To_Hit_Dodje()
    {
        if (Enemy_Switch.Dodje == true)
        {
            ResetTriggers();
            EnemyAnimator.SetTrigger("Dodje");
        }
    }
}
