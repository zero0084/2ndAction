// Card Expansion/Gacha Evolution Ver.1, item 7 - "将来的な属性Build用の
// 基礎を追加してください...ElementTypeをデータとして持てる構造にしてく
// ださい...現段階では複雑な属性相性を作らないでください". Purely a data
// tag on CardDefinition.element for now - no relationships between
// elements (e.g. Fire > Ice) and no gameplay hook reads this yet beyond
// display (see CardDefinition.element's own comment) - the actual Burn/
// Freeze/Chain mechanics this is meant to eventually support are
// explicitly deferred, per the brief's own "今回まだ確定していないもの".
public enum ElementType
{
    None,
    Thunder,
    Fire,
    Ice,
    Wind
}
