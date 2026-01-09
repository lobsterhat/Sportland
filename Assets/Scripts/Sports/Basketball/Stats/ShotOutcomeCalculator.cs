using UnityEngine;
using System.Collections.Generic;

namespace Sportland.Sports.Basketball.Stats
{
    public enum ShotResult
    {
        Swish,
        RimAndIn,
        BackboardAndIn,
        Miss
    }

    public enum RimContact
    {
        FrontRim,
        BackRim,
        LeftRim,
        RightRim,
        Backboard
    }

    public class ShotOutcome
    {
        public ShotResult result;
        public List<RimContact> rimContacts; // Sequence of rim hits
        public Vector2 finalRestPosition; // Where ball ends up if miss

        public ShotOutcome()
        {
            rimContacts = new List<RimContact>();
        }
    }

    public static class ShotOutcomeCalculator
    {
        public static ShotOutcome CalculateOutcome(Vector2 shooterPos, Vector2 hoopPos, float shotAccuracy)
        {
            ShotOutcome outcome = new ShotOutcome();

            // First determine make or miss
            float makeChance = shotAccuracy;
            bool makes = Random.Range(0f, 1f) < makeChance;

            if (makes)
            {
                outcome.result = DetermineMakeType();
                if (outcome.result == ShotResult.RimAndIn)
                {
                    outcome.rimContacts = GenerateRimAndInSequence(shooterPos, hoopPos);
                }
                else if (outcome.result == ShotResult.BackboardAndIn)
                {
                    outcome.rimContacts = GenerateBackboardAndInSequence(shooterPos, hoopPos);
                }
            }
            else
            {
                outcome.result = ShotResult.Miss;
                outcome.rimContacts = GenerateMissSequence(shooterPos, hoopPos, shotAccuracy);
                outcome.finalRestPosition = CalculateFinalRestPosition(hoopPos, outcome.rimContacts);
            }

            return outcome;
        }

        private static ShotResult DetermineMakeType()
        {
            float roll = Random.Range(0f, 1f);

            if (roll < 0.5f)
                return ShotResult.Swish;
            else if (roll < 0.85f)
                return ShotResult.RimAndIn;
            else
                return ShotResult.BackboardAndIn;
        }

        private static List<RimContact> GenerateRimSequence(int minBounces, int maxBounces)
        {
            List<RimContact> contacts = new List<RimContact>();
            int bounceCount = Random.Range(minBounces, maxBounces + 1);

            for (int i = 0; i < bounceCount; i++)
            {
                contacts.Add(GetRandomRimSide());
            }

            return contacts;
        }

        private static RimContact GetApproachRimSide(Vector2 shooterPos, Vector2 hoopPos)
        {
            // Determine which rim edge the ball approaches from
            Vector2 approach = hoopPos - shooterPos;

            // Primary direction is front/back (Y axis in court coordinates)
            if (Mathf.Abs(approach.y) > Mathf.Abs(approach.x) * 0.3f)
            {
                // Approaching primarily from front or back
                return approach.y > 0 ? RimContact.FrontRim : RimContact.BackRim;
            }
            else
            {
                // Approaching from side angle
                return approach.x > 0 ? RimContact.LeftRim : RimContact.RightRim;
            }
        }

        private static RimContact GetOppositeRimSide(RimContact contact)
        {
            switch (contact)
            {
                case RimContact.FrontRim: return RimContact.BackRim;
                case RimContact.BackRim: return RimContact.FrontRim;
                case RimContact.LeftRim: return RimContact.RightRim;
                case RimContact.RightRim: return RimContact.LeftRim;
                default: return contact;
            }
        }

        private static List<RimContact> GenerateRimAndInSequence(Vector2 shooterPos, Vector2 hoopPos)
        {
            List<RimContact> contacts = new List<RimContact>();
            RimContact approachSide = GetApproachRimSide(shooterPos, hoopPos);
            float roll = Random.Range(0f, 1f);

            // 30% chance: Backboard involved before rim
            if (roll < 0.3f)
            {
                contacts.Add(RimContact.Backboard);
                // Ball comes off backboard and hits near rim first
                contacts.Add(approachSide);

                // 50% chance for second rim contact on opposite side before going in
                if (Random.Range(0f, 1f) < 0.5f)
                {
                    contacts.Add(GetOppositeRimSide(approachSide));
                }
            }
            // 40% chance: Rattles around inside rim (2-3 contacts, opposite sides)
            else if (roll < 0.7f)
            {
                // First hit on near rim (outside edge)
                contacts.Add(approachSide);

                // Bounces to opposite side (inside edge)
                contacts.Add(GetOppositeRimSide(approachSide));

                // 50% chance for third rattle
                if (Random.Range(0f, 1f) < 0.5f)
                {
                    contacts.Add(approachSide);
                }
            }
            // 30% chance: Hits top of rim and pops up, then falls in
            else
            {
                // Hit top of near rim - pops ball up
                contacts.Add(approachSide);

                // 60% chance to hit rim again on the way down
                if (Random.Range(0f, 1f) < 0.6f)
                {
                    // Could hit either side on the way down
                    contacts.Add(Random.Range(0f, 1f) < 0.5f ? approachSide : GetOppositeRimSide(approachSide));
                }
            }

            return contacts;
        }

        private static List<RimContact> GenerateBackboardAndInSequence(Vector2 shooterPos, Vector2 hoopPos)
        {
            List<RimContact> contacts = new List<RimContact>();
            RimContact approachSide = GetApproachRimSide(shooterPos, hoopPos);

            // Always hits backboard first
            contacts.Add(RimContact.Backboard);

            // 60% chance to hit rim before going in
            if (Random.Range(0f, 1f) < 0.6f)
            {
                // After backboard, ball hits the near rim
                contacts.Add(approachSide);

                // 30% chance for second rim contact on opposite side
                if (Random.Range(0f, 1f) < 0.3f)
                {
                    contacts.Add(GetOppositeRimSide(approachSide));
                }
            }

            return contacts;
        }

        private static List<RimContact> GenerateMissSequence(Vector2 shooterPos, Vector2 hoopPos, float shotAccuracy)
        {
            List<RimContact> contacts = new List<RimContact>();
            RimContact approachSide = GetApproachRimSide(shooterPos, hoopPos);

            // Airball chance
            float distance = Vector2.Distance(shooterPos, hoopPos);
            float airballChance = Mathf.Lerp(0.05f, 0.2f, distance / 25f) * (1f - shotAccuracy);

            if (Random.Range(0f, 1f) < airballChance)
            {
                // Airball - no rim contacts
                return contacts;
            }

            // Determine first contact - always hits near rim or backboard first
            float roll = Random.Range(0f, 1f);
            if (roll < 0.15f)
            {
                // Hits backboard first
                contacts.Add(RimContact.Backboard);
                // After backboard, usually hits near rim
                if (Random.Range(0f, 1f) < 0.7f)
                {
                    contacts.Add(approachSide);
                }
            }
            else
            {
                // Hits near rim (outside edge) directly
                contacts.Add(approachSide);
            }

            // Chance for additional rim rattles (for misses that rattle out)
            // These can be any side as the ball bounces around
            int maxRattles = Random.Range(0, 3);
            for (int i = 0; i < maxRattles; i++)
            {
                if (Random.Range(0f, 1f) < 0.4f)
                {
                    contacts.Add(GetRandomRimSide());
                }
            }

            return contacts;
        }

        private static RimContact GetRandomRimSide()
        {
            float roll = Random.Range(0f, 1f);

            if (roll < 0.25f)
                return RimContact.FrontRim;
            else if (roll < 0.5f)
                return RimContact.BackRim;
            else if (roll < 0.75f)
                return RimContact.LeftRim;
            else
                return RimContact.RightRim;
        }

        private static Vector2 CalculateFinalRestPosition(Vector2 hoopPos, List<RimContact> contacts)
        {
            if (contacts.Count == 0)
            {
                // Airball - lands somewhere past the hoop
                return hoopPos + new Vector2(
                    Random.Range(-2f, 2f),
                    Random.Range(-2f, 1f)
                );
            }

            // Final contact determines general direction
            RimContact lastContact = contacts[contacts.Count - 1];
            Vector2 bounceDirection = GetBounceDirection(lastContact);

            // Distance based on how many contacts (more = less energy = closer)
            float distance = Mathf.Lerp(3f, 1f, contacts.Count / 4f);
            distance *= Random.Range(0.7f, 1.3f);

            return hoopPos + bounceDirection * distance;
        }

        private static Vector2 GetBounceDirection(RimContact contact)
        {
            switch (contact)
            {
                case RimContact.FrontRim:
                    return new Vector2(Random.Range(-0.3f, 0.3f), -1f).normalized;
                case RimContact.BackRim:
                    return new Vector2(Random.Range(-0.3f, 0.3f), 1f).normalized;
                case RimContact.LeftRim:
                    return new Vector2(-1f, Random.Range(-0.3f, 0.3f)).normalized;
                case RimContact.RightRim:
                    return new Vector2(1f, Random.Range(-0.3f, 0.3f)).normalized;
                case RimContact.Backboard:
                    return new Vector2(Random.Range(-0.5f, 0.5f), -1f).normalized;
                default:
                    return Vector2.down;
            }
        }
    }
}