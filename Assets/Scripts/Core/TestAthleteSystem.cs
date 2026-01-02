using NUnit.Framework.Internal;
using Sportland.Core;
using System.Numerics;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class TestAthleteSystem : MonoBehaviour
{
    void Start()
    {
        // Wait a frame for CoreGameManager to initialize
        Invoke("TestAthletes", 0.1f);
    }

    void TestAthletes()
    {
        Debug.Log("=== TESTING ATHLETE SYSTEM ===");

        var athletes = CoreGameManager.Instance.GetAllAthletes();

        Debug.Log($"Total athletes loaded: {athletes.Count}");

        foreach (var athlete in athletes)
        {
            Debug.Log($"");
            Debug.Log($"Athlete: {athlete.GetFullName()}");
            Debug.Log($"ID: {athlete.athleteID}");
            Debug.Log($"Speed: {athlete.speed} ({athlete.GetLetterGrade(athlete.speed)})");
            Debug.Log($"Strength: {athlete.strength} ({athlete.GetLetterGrade(athlete.strength)})");
            Debug.Log($"Agility: {athlete.agility} ({athlete.GetLetterGrade(athlete.agility)})");
            Debug.Log($"Reaction: {athlete.reactionTime} ({athlete.GetLetterGrade(athlete.reactionTime)})");
            Debug.Log($"Fatigue: {athlete.fatigue}");
        }

        Debug.Log("=== TEST COMPLETE ===");
    }
}