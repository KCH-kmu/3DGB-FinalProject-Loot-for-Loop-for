using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class InventoryGridSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    InventoryView view;
    public bool IsBag { get; private set; }
    public int SlotIndex { get; set; }
    public void Initialize(InventoryView owner, bool bag, int slot) { view = owner; IsBag = bag; SlotIndex = slot; }
    public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) view.BeginDrag(IsBag, SlotIndex, e); }
    public void OnDrag(PointerEventData e) => view.Drag(e);
    public void OnEndDrag(PointerEventData e) => view.CancelDrag();
    public void OnDrop(PointerEventData e) => view.Drop(IsBag, SlotIndex);
    public void OnPointerEnter(PointerEventData e) => view.Hover(this, true);
    public void OnPointerExit(PointerEventData e) => view.Hover(this, false);
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right) view.Split(IsBag, SlotIndex);
        else if (e.button == PointerEventData.InputButton.Left &&
            (e.clickCount == 2 || (Keyboard.current != null && Keyboard.current.shiftKey.isPressed))) view.Quick(IsBag, SlotIndex);
    }
}
