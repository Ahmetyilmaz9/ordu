using UnityEngine;

// Oyundaki birim tipleri. Bölük ağda sadece bu listedeki indeksini taşır; istatistikler her iki tarafta da
// aynı asset'ten okunur. M6'da ordu kurma ekranı da bu listeden seçecek.
[CreateAssetMenu(menuName = "Ordu/Unit Roster")]
public class UnitRoster : ScriptableObject
{
    public UnitDefinition[] units;
}
