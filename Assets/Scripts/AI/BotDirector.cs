using UnityEngine;

/// <summary>
/// Runs every bot's frame from one Update (EnemyBot.TickAll): a MonoBehaviour Update per bot costs a call from the engine each,
/// 200 of them add up. Made by the first bot that wakes in Play mode; goes with the scene.
/// </summary>
public class BotDirector : MonoBehaviour
{
    void Update() { EnemyBot.TickAll(Time.deltaTime); }
}
