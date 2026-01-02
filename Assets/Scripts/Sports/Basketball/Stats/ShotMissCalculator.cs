using UnityEngine;

namespace Sportland.Sports.Basketball.Stats
{
    public enum MissType
    {
        FrontRim,
        BackRim,
        LeftRim,
        RightRim,
        Backboard,
        BackboardThenRim,
        Airball
    }

    public static class ShotMissCalculator
    {
        public static MissType DetermineMissType(Vector2 shooterPos, Vector2 hoopPos, float shotAccuracy)
        {
            float distance = Vector2.Distance(shooterPos, hoopPos);

            float roll = Random.Range(0f, 1f);

            float airballChance = Mathf.Lerp(0.05f, 0.2f, distance / 25f);
            airballChance *= (1f - shotAccuracy);

            if (roll < airballChance)
                return MissType.Airball;

            roll = Random.Range(0f, 1f);

            if (roll < 0.15f)
                return MissType.Backboard;
            else if (roll < 0.25f)
                return MissType.BackboardThenRim;
            else if (roll < 0.45f)
                return MissType.FrontRim;
            else if (roll < 0.65f)
                return MissType.BackRim;
            else if (roll < 0.825f)
                return MissType.LeftRim;
            else
                return MissType.RightRim;
        }

        public static MissType GetRandomRimSide()
        {
            float roll = Random.Range(0f, 1f);

            if (roll < 0.25f)
                return MissType.FrontRim;
            else if (roll < 0.5f)
                return MissType.BackRim;
            else if (roll < 0.75f)
                return MissType.LeftRim;
            else
                return MissType.RightRim;
        }

        public static Vector2 CalculateBounceVelocity(MissType missType, Vector2 hoopPos, Vector2 shooterPos, float incomingSpeed, int bounceCount)
        {
            Vector2 toShooter = (shooterPos - hoopPos).normalized;

            // Much more energy loss - ball should stay near rim
            float energyRetention = Mathf.Pow(0.5f, bounceCount);
            float bounceSpeed = incomingSpeed * 0.15f * energyRetention; // Reduced from 0.4f

            float angleVariation = Random.Range(-25f, 25f);

            switch (missType)
            {
                case MissType.FrontRim:
                    return RotateVector(toShooter, angleVariation) * bounceSpeed * Random.Range(0.5f, 0.8f);

                case MissType.BackRim:
                    return RotateVector(-toShooter, angleVariation) * bounceSpeed * Random.Range(0.4f, 0.7f);

                case MissType.LeftRim:
                    Vector2 left = new Vector2(-toShooter.y, toShooter.x);
                    return RotateVector(left, angleVariation) * bounceSpeed * Random.Range(0.5f, 0.8f);

                case MissType.RightRim:
                    Vector2 right = new Vector2(toShooter.y, -toShooter.x);
                    return RotateVector(right, angleVariation) * bounceSpeed * Random.Range(0.5f, 0.8f);

                case MissType.Backboard:
                    return RotateVector(toShooter, angleVariation * 0.5f) * bounceSpeed * Random.Range(0.6f, 1.0f);

                case MissType.BackboardThenRim:
                    float randomAngle = Random.Range(0f, 360f);
                    Vector2 randomDir = RotateVector(Vector2.right, randomAngle);
                    return randomDir * bounceSpeed * Random.Range(0.2f, 0.4f);

                case MissType.Airball:
                default:
                    return Vector2.zero;
            }
        }

        public static float CalculateBounceVerticalVelocity(MissType missType, int bounceCount)
        {
            float energyRetention = Mathf.Pow(0.7f, bounceCount); // Changed from 0.6f for more pop

            float baseVelocity;
            switch (missType)
            {
                case MissType.FrontRim:
                    baseVelocity = Random.Range(5f, 7f);
                    break;
                case MissType.BackRim:
                    baseVelocity = Random.Range(4f, 6f);
                    break;
                case MissType.LeftRim:
                case MissType.RightRim:
                    baseVelocity = Random.Range(5f, 8f);
                    break;
                case MissType.Backboard:
                    baseVelocity = Random.Range(3f, 5f);
                    break;
                case MissType.BackboardThenRim:
                    baseVelocity = Random.Range(6f, 9f);
                    break;
                case MissType.Airball:
                default:
                    baseVelocity = 0f;
                    break;
            }

            return baseVelocity * energyRetention;
        }

        private static Vector2 RotateVector(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(
                v.x * cos - v.y * sin,
                v.x * sin + v.y * cos
            );
        }
    }
}