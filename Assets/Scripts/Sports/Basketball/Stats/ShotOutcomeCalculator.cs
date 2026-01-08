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
                    outcome.rimContacts = GenerateRimAndInSequence();
                }
                else if (outcome.result == ShotResult.BackboardAndIn)
                {
                    outcome.rimContacts = GenerateBackboardAndInSequence();
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

        private static List<RimContact> GenerateRimAndInSequence()
        {
            List<RimContact> contacts = new List<RimContact>();
            float roll = Random.Range(0f, 1f);

            // 30% chance: Backboard involved before rim
            if (roll < 0.3f)
            {
                contacts.Add(RimContact.Backboard);
                // Ball comes off backboard and hits rim 1-2 times before going in
                int rimHits = Random.Range(1, 3);
                for (int i = 0; i < rimHits; i++)
                {
                    contacts.Add(GetRandomRimSide());
                }
            }
            // 40% chance: Rattles around inside rim (2-3 contacts, opposite sides)
            else if (roll < 0.7f)
            {
                // Start with front or back rim
                RimContact firstContact = Random.Range(0f, 1f) < 0.5f ? RimContact.FrontRim : RimContact.BackRim;
                contacts.Add(firstContact);

                // Opposite side
                contacts.Add(firstContact == RimContact.FrontRim ? RimContact.BackRim : RimContact.FrontRim);

                // 50% chance for third rattle (left or right)
                if (Random.Range(0f, 1f) < 0.5f)
                {
                    contacts.Add(Random.Range(0f, 1f) < 0.5f ? RimContact.LeftRim : RimContact.RightRim);
                }
            }
            // 30% chance: Bounces on top of rim then falls in (1-2 bounces)
            else
            {
                // Hit front or back rim (top), can bounce up
                contacts.Add(Random.Range(0f, 1f) < 0.6f ? RimContact.FrontRim : RimContact.BackRim);

                // 60% chance for second bounce
                if (Random.Range(0f, 1f) < 0.6f)
                {
                    contacts.Add(GetRandomRimSide());
                }
            }

            return contacts;
        }

        private static List<RimContact> GenerateBackboardAndInSequence()
        {
            List<RimContact> contacts = new List<RimContact>();

            // Always hits backboard first
            contacts.Add(RimContact.Backboard);

            // 60% chance to hit rim before going in
            if (Random.Range(0f, 1f) < 0.6f)
            {
                contacts.Add(GetRandomRimSide());

                // 30% chance for second rim contact
                if (Random.Range(0f, 1f) < 0.3f)
                {
                    contacts.Add(GetRandomRimSide());
                }
            }

            return contacts;
        }

        private static List<RimContact> GenerateMissSequence(Vector2 shooterPos, Vector2 hoopPos, float shotAccuracy)
        {
            List<RimContact> contacts = new List<RimContact>();

            // Airball chance
            float distance = Vector2.Distance(shooterPos, hoopPos);
            float airballChance = Mathf.Lerp(0.05f, 0.2f, distance / 25f) * (1f - shotAccuracy);

            if (Random.Range(0f, 1f) < airballChance)
            {
                // Airball - no rim contacts
                return contacts;
            }

            // Determine first contact
            float roll = Random.Range(0f, 1f);
            if (roll < 0.15f)
            {
                contacts.Add(RimContact.Backboard);
                // After backboard, usually hits rim
                if (Random.Range(0f, 1f) < 0.7f)
                {
                    contacts.Add(GetRandomRimSide());
                }
            }
            else
            {
                contacts.Add(GetRandomRimSide());
            }

            // Chance for additional rim rattles (for misses that rattle out)
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