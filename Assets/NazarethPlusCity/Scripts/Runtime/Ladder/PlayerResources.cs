using UnityEngine;

namespace NazarethPlusCity
{
    [DisallowMultipleComponent]
    public class PlayerResources : MonoBehaviour
    {
        public int wood;
        public int coins;
        public bool initialized;

        public int Wood => wood;
        public int Coins => coins;

        public void ConfigureStart(int startWood, int startCoins)
        {
            wood = Mathf.Max(0, startWood);
            coins = Mathf.Max(0, startCoins);
            initialized = true;
        }

        public bool CanAffordWood(int amount)
        {
            return amount <= 0 || wood >= amount;
        }

        public bool TrySpendWood(int amount)
        {
            if (amount <= 0)
                return true;
            if (wood < amount)
                return false;
            wood -= amount;
            return true;
        }

        public void AddWood(int amount)
        {
            if (amount > 0)
                wood += amount;
        }

        public static float AverageClimbHeight()
        {
            var buildings = Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (buildings == null || buildings.Length == 0)
                return 9f;
            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < buildings.Length; i++)
            {
                if (buildings[i] == null)
                    continue;
                sum += buildings[i].ActiveHeight;
                count++;
            }

            if (count == 0)
                return 9f;
            return (float)(sum / count);
        }
    }
}
