using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Woodsmen.Players
{
    /// <summary>
    /// Universal input reader for all humanoid characters (Lumberjack, Warrior, etc.).
    /// Exposes standard top-down inputs: Move, Look, PrimaryAction, and SecondaryAction.
    /// Eliminates Inspector string clutter and provides clean lifecycle management (zero memory leaks, zero GC).
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Optional custom InputActionAsset. If left empty, fallback actions will be initialized automatically.")]
        [SerializeField] private InputActionAsset customInputActions;

        // Action map & action name constants (No Inspector clutter)
        private const string ActionMapName = "Player";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string PrimaryActionName = "PrimaryAction";
        private const string SecondaryActionName = "SecondaryAction";
        private const string InventoryActionName = "Inventory";

        private InputActionMap _actionMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _primaryAction;
        private InputAction _secondaryAction;
        private InputAction _inventoryAction;

        private bool _isInitialized;

        /// <summary>
        /// Movement input vector from Keyboard (WASD/Arrows) or Gamepad left stick.
        /// </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>
        /// Screen position of the pointer/mouse cursor.
        /// </summary>
        public Vector2 PointerPosition
        {
            get
            {
                if (Mouse.current != null) return Mouse.current.position.ReadValue();
                return _pointerPosition;
            }
            private set => _pointerPosition = value;
        }
        private Vector2 _pointerPosition;

        /// <summary>
        /// True while Primary Action (LMB / Right Trigger) is held.
        /// </summary>
        public bool IsPrimaryActionHeld
        {
            get
            {
                if (_primaryAction != null && _primaryAction.IsPressed()) return true;
                if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
                return _isPrimaryActionHeld;
            }
            private set => _isPrimaryActionHeld = value;
        }
        private bool _isPrimaryActionHeld;

        /// <summary>
        /// True while Secondary Action (RMB / Left Trigger) is held.
        /// </summary>
        public bool IsSecondaryActionHeld
        {
            get
            {
                if (_secondaryAction != null && _secondaryAction.IsPressed()) return true;
                if (Mouse.current != null && Mouse.current.rightButton.isPressed) return true;
                return _isSecondaryActionHeld;
            }
            private set => _isSecondaryActionHeld = value;
        }
        private bool _isSecondaryActionHeld;

        /// <summary>
        /// True if either primary or secondary actions are active (used by Locomotion to face the cursor).
        /// </summary>
        public bool IsAiming => IsPrimaryActionHeld || IsSecondaryActionHeld;

        // Events for character actions (e.g. Lumberjack chopping, Warrior attacking)
        public event Action OnPrimaryActionStarted;
        public event Action OnPrimaryActionCanceled;
        public event Action OnSecondaryActionStarted;
        public event Action OnSecondaryActionCanceled;

        // Inventory action events (Tab / Gamepad Select)
        public event Action OnInventoryActionTriggered;
        public static event Action OnInventoryToggleRequested;

        private void Awake()
        {
            InitializeActions();
        }

        private void OnEnable()
        {
            if (!_isInitialized)
            {
                InitializeActions();
            }

            EnableActions();
        }

        private void OnDisable()
        {
            DisableActions();
        }

        private void OnDestroy()
        {
            UnbindActions();
        }

        private void InitializeActions()
        {
            if (_isInitialized) return;

            if (customInputActions != null)
            {
                _actionMap = customInputActions.FindActionMap(ActionMapName, false);
                if (_actionMap != null)
                {
                    _moveAction = _actionMap.FindAction(MoveActionName, false);
                    _lookAction = _actionMap.FindAction(LookActionName, false);
                    _primaryAction = _actionMap.FindAction(PrimaryActionName, false) ?? _actionMap.FindAction("AimPrimary", false);
                    _secondaryAction = _actionMap.FindAction(SecondaryActionName, false) ?? _actionMap.FindAction("AimSecondary", false);
                    _inventoryAction = _actionMap.FindAction(InventoryActionName, false);
                }
            }

            // Ensure Inventory action exists on the map
            if (_actionMap != null && _inventoryAction == null)
            {
                _inventoryAction = _actionMap.AddAction(InventoryActionName, InputActionType.Button);
                _inventoryAction.AddBinding("<Keyboard>/tab");
                _inventoryAction.AddBinding("<Gamepad>/select");
            }

            // Programmatic fallback if no asset or missing map
            if (_actionMap == null || _moveAction == null)
            {
                CreateFallbackActions();
            }

            BindActionEvents();
            _isInitialized = true;
        }

        private void CreateFallbackActions()
        {
            _actionMap = new InputActionMap("PlayerRuntimeMap");

            // Move: 2D Composite for WASD / Arrows + Gamepad Left Stick
            _moveAction = _actionMap.AddAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            // Look: Pointer position
            _lookAction = _actionMap.AddAction("Look", InputActionType.Value);
            _lookAction.AddBinding("<Pointer>/position");

            // Primary Action: Left Mouse / Right Trigger
            _primaryAction = _actionMap.AddAction("PrimaryAction", InputActionType.Button);
            _primaryAction.AddBinding("<Mouse>/leftButton");
            _primaryAction.AddBinding("<Gamepad>/rightTrigger");

            // Secondary Action: Right Mouse / Left Trigger
            _secondaryAction = _actionMap.AddAction("SecondaryAction", InputActionType.Button);
            _secondaryAction.AddBinding("<Mouse>/rightButton");
            _secondaryAction.AddBinding("<Gamepad>/leftTrigger");

            // Inventory Action: Tab / Gamepad Select
            _inventoryAction = _actionMap.AddAction(InventoryActionName, InputActionType.Button);
            _inventoryAction.AddBinding("<Keyboard>/tab");
            _inventoryAction.AddBinding("<Gamepad>/select");
        }

        private void BindActionEvents()
        {
            if (_moveAction != null)
            {
                _moveAction.performed += OnMovePerformed;
                _moveAction.canceled += OnMoveCanceled;
            }

            if (_lookAction != null)
            {
                _lookAction.performed += OnLookPerformed;
            }

            if (_primaryAction != null)
            {
                _primaryAction.performed += OnPrimaryPerformed;
                _primaryAction.canceled += OnPrimaryCanceled;
            }

            if (_secondaryAction != null)
            {
                _secondaryAction.performed += OnSecondaryPerformed;
                _secondaryAction.canceled += OnSecondaryCanceled;
            }

            if (_inventoryAction != null)
            {
                _inventoryAction.performed += OnInventoryPerformed;
            }
        }

        private void UnbindActions()
        {
            if (_moveAction != null)
            {
                _moveAction.performed -= OnMovePerformed;
                _moveAction.canceled -= OnMoveCanceled;
            }

            if (_lookAction != null)
            {
                _lookAction.performed -= OnLookPerformed;
            }

            if (_primaryAction != null)
            {
                _primaryAction.performed -= OnPrimaryPerformed;
                _primaryAction.canceled -= OnPrimaryCanceled;
            }

            if (_secondaryAction != null)
            {
                _secondaryAction.performed -= OnSecondaryPerformed;
                _secondaryAction.canceled -= OnSecondaryCanceled;
            }

            if (_inventoryAction != null)
            {
                _inventoryAction.performed -= OnInventoryPerformed;
            }
        }

        private void EnableActions()
        {
            _actionMap?.Enable();
        }

        private void DisableActions()
        {
            _actionMap?.Disable();
            MoveInput = Vector2.zero;
            IsPrimaryActionHeld = false;
            IsSecondaryActionHeld = false;
        }

        private void OnMovePerformed(InputAction.CallbackContext context) => MoveInput = context.ReadValue<Vector2>();
        private void OnMoveCanceled(InputAction.CallbackContext context) => MoveInput = Vector2.zero;

        private void OnLookPerformed(InputAction.CallbackContext context) => PointerPosition = context.ReadValue<Vector2>();

        private void OnPrimaryPerformed(InputAction.CallbackContext context)
        {
            IsPrimaryActionHeld = true;
            OnPrimaryActionStarted?.Invoke();
        }

        private void OnPrimaryCanceled(InputAction.CallbackContext context)
        {
            IsPrimaryActionHeld = false;
            OnPrimaryActionCanceled?.Invoke();
        }

        private void OnSecondaryPerformed(InputAction.CallbackContext context)
        {
            IsSecondaryActionHeld = true;
            OnSecondaryActionStarted?.Invoke();
        }

        private void OnSecondaryCanceled(InputAction.CallbackContext context)
        {
            IsSecondaryActionHeld = false;
            OnSecondaryActionCanceled?.Invoke();
        }

        public void TriggerInventoryToggle()
        {
            OnInventoryActionTriggered?.Invoke();
            OnInventoryToggleRequested?.Invoke();
        }

        private void OnInventoryPerformed(InputAction.CallbackContext context)
        {
            TriggerInventoryToggle();
        }
    }
}
