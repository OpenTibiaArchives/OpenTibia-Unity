﻿using OpenTibiaUnity.Core.Appearances;
using OpenTibiaUnity.Core.Components;
using OpenTibiaUnity.Core.Container;
using OpenTibiaUnity.Core.Game;
using OpenTibiaUnity.Core.Input.GameAction;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTibiaUnity.Modules.Container
{
    public class ContainerWindow : Core.Components.Base.MiniWindow, IUseWidget, IMoveWidget, IWidgetContainerWidget
    {
        [SerializeField] private RawImage _iconImage = null;
        [SerializeField] private OTU_ScrollRect _itemsScrollRect = null;
        [SerializeField] private Button _upButton = null;

        private ContainerView _containerView = null;
        private int _rows = -1;
        private int _numberOfSlots = 0;
        private int _slotUnderMouse = -1;

        private ItemView[] _itemViews;
        private RenderTexture _slotsRenderTexture;
        private ObjectDragImpl<ContainerWindow> _dragHandler;

        protected override void Awake() {
            base.Awake();

            _dragHandler = new ObjectDragImpl<ContainerWindow>(this);
            ObjectMultiUseHandler.RegisterContainer(this);

            OpenTibiaUnity.InputHandler.AddMouseUpListener(Core.Utils.EventImplPriority.Default, OnMouseUp);
            OpenTibiaUnity.GameManager.onGameEnd.AddListener(OnGameEnd);

            _upButton.onClick.AddListener(OnUpButtonClick);
        }

        protected void OnGUI() {
            if (Event.current.type != EventType.Repaint)
                return;

            var gameManager = OpenTibiaUnity.GameManager;
            publicStartMouseAction(Input.mousePosition, MouseButton.None, false, true);

            if (!_slotsRenderTexture)
                return;

            Vector2 zoom = new Vector2(Screen.width / (float)_slotsRenderTexture.width, Screen.height / (float)_slotsRenderTexture.height);

            _slotsRenderTexture.Release();
            RenderTexture.active = _slotsRenderTexture;
            for (int i = 0; i < 4; i++) {
                for (int j = 0; j < _rows; j++) {
                    int index = j * 4 + i;
                    if (index < _containerView.NumberOfTotalObjects) {
                        var @object = _containerView.GetObject(index + _containerView.IndexOfFirstObject);
                        if (@object) {
                            @object.Animate(OpenTibiaUnity.TicksMillis);
                            @object.Draw(new Vector2(Constants.FieldSize * i, Constants.FieldSize * j), zoom, 0, 0, 0);
                        }
                    }
                }
            }

            int iconColumn = _numberOfSlots % 4;
            int iconRow = _numberOfSlots / 4;
            _containerView.Icon.Draw(new Vector2(Constants.FieldSize * iconColumn, Constants.FieldSize * iconRow), zoom, 0, 0, 0);

            RenderTexture.active = null;
        }

        protected void OnMouseUp(Event e, MouseButton mouseButton, bool repeat) {
            if (publicStartMouseAction(e.mousePosition, mouseButton, true, false))
                e.Use();
        }

        private void OnGameEnd() {
            var gameManager = OpenTibiaUnity.GameManager;
            gameManager.InvokeOnMainThread(() => gameManager.onGameEnd.RemoveListener(OnGameEnd));

            CloseWithoutNotifying();
        }

        private void OnUpButtonClick() {
            var protocolGame = OpenTibiaUnity.ProtocolGame;
            if (!!protocolGame && protocolGame.IsGameRunning)
                protocolGame.SendUpContainer(_containerView.Id);
        }

        private bool publicStartMouseAction(Vector3 mousePosition, MouseButton mouseButton, bool applyAction = false, bool updateCursor = false) {
            var gameManager = OpenTibiaUnity.GameManager;
            if (!_mouseCursorOverRenderer || !gameManager.GameCanvas.gameObject.activeSelf || gameManager.GamePanelBlocker.gameObject.activeSelf)
                return false;

            var @object = GetObjectUnderMouse();
            var eventModifiers = OpenTibiaUnity.InputHandler.GetRawEventModifiers();
            var action = DetermineAction(mousePosition, mouseButton, eventModifiers, @object, applyAction, updateCursor);
            return action != AppearanceActions.None;
        }

        protected AppearanceActions DetermineAction(Vector3 mousePosition, MouseButton mouseButton, EventModifiers eventModifiers, ObjectInstance @object, bool applyAction = false, bool updateCursor = false) {
            if (!@object)
                return AppearanceActions.None;

            if (updateCursor)
                updateCursor = OpenTibiaUnity.GameManager.ClientVersion >= 1100;

            var inputHandler = OpenTibiaUnity.InputHandler;
            if (inputHandler.IsMouseButtonDragged(MouseButton.Left) || inputHandler.IsMouseButtonDragged(MouseButton.Right))
                return AppearanceActions.None;

            var action = AppearanceActions.None;
            var optionStorage = OpenTibiaUnity.OptionStorage;

            if (optionStorage.MousePreset == MousePresets.LeftSmartClick) {
                if (eventModifiers == EventModifiers.None || eventModifiers == EventModifiers.Control) {
                    if (mouseButton == MouseButton.Left || mouseButton == MouseButton.None) {
                        action = AppearanceActions.Open;
                        var defaultAction = @object.Type.DefaultAction;
                        if (defaultAction == Protobuf.Shared.PlayerAction.Open)
                            action = AppearanceActions.Open;
                        else if (defaultAction == Protobuf.Shared.PlayerAction.Look)
                            action = AppearanceActions.Look;
                        else if (defaultAction == Protobuf.Shared.PlayerAction.Use)
                            action = AppearanceActions.Use;
                        else if (@object.Type.IsContainer)
                            action = AppearanceActions.Open;
                        else if (@object.Type.IsForceUse || @object.Type.IsMultiUse || @object.Type.IsUsable)
                            action = AppearanceActions.Use;
                        else
                            action = AppearanceActions.Look;
                    } else if (mouseButton == MouseButton.Right && eventModifiers == EventModifiers.None) {
                        action = AppearanceActions.ContextMenu;
                    }
                } else if (eventModifiers == EventModifiers.Shift) {
                    if (mouseButton == MouseButton.Left || mouseButton == MouseButton.None)
                        action = AppearanceActions.Look;
                }

            } else if (optionStorage.MousePreset == MousePresets.Classic) {
                if (mouseButton == MouseButton.Left || mouseButton == MouseButton.None) {
                    if (eventModifiers == EventModifiers.Shift)
                        action = AppearanceActions.Look;
                    else if (eventModifiers == EventModifiers.Control)
                        action = AppearanceActions.ContextMenu;
                    else if (eventModifiers == EventModifiers.Alt)
                        action = AppearanceActions.Open;
                } else if (mouseButton == MouseButton.Right) {
                    if (eventModifiers == EventModifiers.None)
                        action = AppearanceActions.Open;
                    else if (eventModifiers == EventModifiers.Control)
                        action = AppearanceActions.ContextMenu;
                } else if (mouseButton == MouseButton.Both) {
                    action = AppearanceActions.Look;
                }
            } else if (optionStorage.MousePreset == MousePresets.Regular) {
                if (mouseButton == MouseButton.Left || mouseButton == MouseButton.None) {
                    if (eventModifiers == EventModifiers.Shift)
                        action = AppearanceActions.Look;
                    else if (eventModifiers == EventModifiers.Control)
                        action = AppearanceActions.Open;
                    else if ((eventModifiers & EventModifiers.Alt) != 0 && (eventModifiers & EventModifiers.Shift) == 0)
                        action = AppearanceActions.ContextMenu;
                } else if (mouseButton == MouseButton.Right) {
                    if (eventModifiers == EventModifiers.None)
                        action = AppearanceActions.ContextMenu;
                    else if (eventModifiers == EventModifiers.Control)
                        action = AppearanceActions.Open;
                }
            }

            if (updateCursor)
                OpenTibiaUnity.GameManager.CursorController.SetCursorState(action, CursorPriority.Medium);

            if (applyAction) {
                var absolutePosition = new Vector3Int(65535, 64 + _containerView.Id, _slotUnderMouse);
                switch (action) {
                    case AppearanceActions.None: break;
                    case AppearanceActions.ContextMenu:
                        OpenTibiaUnity.CreateObjectContextMenu(absolutePosition, @object, absolutePosition.z, @object, absolutePosition.z, null)
                            .Display(mousePosition);
                        break;
                    case AppearanceActions.Look:
                        new LookActionImpl(absolutePosition, @object.Type, absolutePosition.z).Perform();
                        break;
                    case AppearanceActions.Use:
                        if (@object.Type.IsMultiUse)
                            ObjectMultiUseHandler.Activate(absolutePosition, @object, absolutePosition.z);
                        else
                            GameActionFactory.CreateUseAction(absolutePosition, @object.Type, absolutePosition.z, Vector3Int.zero, null, 0, UseActionTarget.Auto).Perform();
                        break;
                    case AppearanceActions.Open:
                        GameActionFactory.CreateUseAction(absolutePosition, @object.Type, absolutePosition.z, Vector3Int.zero, null, 0, UseActionTarget.Auto).Perform();
                        break;
                    case AppearanceActions.Unset:
                        break;
                }
            }

            return action;
        }

        public void UpdateProperties(ContainerView containerView) {
            containerView.onObjectAdded.AddListener(OnAddedObject);
            containerView.onObjectChanged.AddListener(OnChangedObject);
            containerView.onObjectRemoved.AddListener(OnRemovedObject);

            if (_itemViews != null) {
                foreach (var itemView in _itemViews)
                    Destroy(itemView.gameObject);

                _itemViews = null;
            }

            if (_slotsRenderTexture != null) {
                _slotsRenderTexture.Release();
                _slotsRenderTexture.DiscardContents();
                _slotsRenderTexture = null;
            }

            _numberOfSlots = containerView.NumberOfSlotsPerPage;
            _rows = (int)Mathf.Ceil((_numberOfSlots + 1) / 4f); // extra slot for the container's icon
            _slotsRenderTexture = new RenderTexture(Constants.FieldSize * 4, Constants.FieldSize * _rows, 0, RenderTextureFormat.ARGB32);
            _itemViews = new ItemView[_numberOfSlots];
            _containerView = containerView;

            int paginationHeight = 0;
            if (containerView.IsPaginationEnabled)
                paginationHeight += 25;

            _minContentHeight = 34 + paginationHeight;

            int effectiveRows = (int)Mathf.Ceil(_numberOfSlots / 4f);
            _maxContentHeight = effectiveRows * 34 + (effectiveRows - 1) * 3 + paginationHeight;

            int activeRows = (int)Mathf.Ceil(containerView.NumberOfTotalObjects / 4f);
            _preferredContentHeight = activeRows * 34 + (activeRows - 1) * 3 + paginationHeight;
            
            _upButton.gameObject.SetActive(containerView.IsSubContainer);

            _titleLabel.SetText(containerView.Name);

            float w = (float)Constants.FieldSize / _slotsRenderTexture.width;
            float h = (float)Constants.FieldSize / _slotsRenderTexture.height;
            for (int i = 0; i < _numberOfSlots; i++) {
                var slot = i; // obtain a local variable to hook the slot under mouse correctly!
                var itemView = Instantiate(ModulesManager.Instance.ItemViewPrefab, _itemsScrollRect.content);
                itemView.onPointerEnter.AddListener((ClothSlots _) => _slotUnderMouse = slot);
                itemView.onPointerExit.AddListener((ClothSlots _) => _slotUnderMouse = -1);
                
                itemView.itemImage.enabled = false;
                itemView.itemImage.texture = _slotsRenderTexture;
                
                itemView.itemImage.uvRect = new Rect(w * (i % 4), 1 - h * (i / 4 + 1), w, h);
                _itemViews[i] = itemView;
            }
            
            _iconImage.enabled = true;
            _iconImage.texture = _slotsRenderTexture;
            _iconImage.uvRect = new Rect(w * (_numberOfSlots % 4), 1 - h * (_numberOfSlots / 4 + 1), w, h);
            UpdateLayout();
        }

        protected void OnAddedObject(ContainerView _, int index, ObjectInstance @object) {
            index -= _containerView.IndexOfFirstObject;
            for (int i = index; i < _containerView.NumberOfTotalObjects; i++) {
                @object = _containerView.GetObject(i + _containerView.IndexOfFirstObject);
                var itemView = _itemViews[i];
                itemView.itemImage.enabled = true;
                itemView.showAmount = @object.Data > 1;
                itemView.objectAmount = (int)@object.Data;
            }
        }

        private void OnChangedObject(ContainerView _, int index, ObjectInstance @object) {
            var itemView = _itemViews[index - _containerView.IndexOfFirstObject];
            itemView.itemImage.enabled = true;
            itemView.showAmount = @object.Data > 1;
            itemView.objectAmount = (int)@object.Data;
        }

        protected void OnRemovedObject(ContainerView _, int index, ObjectInstance appendObject) {
            index -= _containerView.IndexOfFirstObject;
            for (int i = index; i < _containerView.NumberOfTotalObjects; i++) {
                var @object = _containerView.GetObject(i + _containerView.IndexOfFirstObject);
                var itemView = _itemViews[i];
                itemView.itemImage.enabled = true;
                itemView.showAmount = @object.Data > 1;
                itemView.objectAmount = (int)@object.Data;
            }
        }


        public override void Close() {
            base.Close();

            var protocolGame = OpenTibiaUnity.ProtocolGame;
            if (!!protocolGame && protocolGame.IsGameRunning)
                protocolGame.SendCloseContainer(_containerView.Id);
        }

        public void CloseWithoutNotifying() {
            base.Close();
        }

        public ObjectInstance GetObjectUnderMouse() {
            if (_slotUnderMouse == -1)
                return null;

            if (_slotUnderMouse >= _containerView.NumberOfTotalObjects)
                return null;

            return _containerView.GetObject(_slotUnderMouse + _containerView.IndexOfFirstObject);
        }

        public bool Matches(ContainerView containerView) {
            return _containerView != null && _containerView.Id == containerView.Id;
        }

        public int GetTopObjectUnderPoint(Vector3 mousePosition, out ObjectInstance @object) {
            @object = GetObjectUnderMouse();
            return _slotUnderMouse;
        }

        public int GetUseObjectUnderPoint(Vector3 mousePosition, out ObjectInstance @object) {
            @object = GetObjectUnderMouse();
            return _slotUnderMouse;
        }

        public int GetMultiUseObjectUnderPoint(Vector3 mousePosition, out ObjectInstance @object) {
            @object = GetObjectUnderMouse();
            if (!@object.Type.IsMultiUse) {
                @object = null;
                return -1;
            }

            return _slotUnderMouse;
        }

        public int GetMoveObjectUnderPoint(Vector3 mousePosition, out ObjectInstance @object) {
            return GetUseObjectUnderPoint(mousePosition, out @object);
        }

        public Vector3Int? MousePositionToAbsolutePosition(Vector3 mousePosition) {
            if (_slotUnderMouse == -1)
                return null;

            return new Vector3Int(65535, 64 + _containerView.Id, _slotUnderMouse);
        }

        public Vector3Int? MousePositionToMapPosition(Vector3 mousePosition) {
            return null;
        }
    }
}
