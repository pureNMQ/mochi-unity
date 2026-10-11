namespace Mochi.Unity.GUI
{
    /// <summary>Routes player back input to the Modal stack before the Page stack.</summary>
    public sealed class NavigationService
    {
        /// <summary>Creates a navigator over the supplied Page and Modal services.</summary>
        public NavigationService(PageService pages, ModalService modals)
        {
            Pages = pages;
            Modals = modals;
        }

        /// <summary>Gets the Page stack service.</summary>
        public PageService Pages { get; }
        /// <summary>Gets the Modal stack service.</summary>
        public ModalService Modals { get; }

        /// <summary>Closes the top Modal, or the top Page when no Modal is present.</summary>
        public bool Back()
        {
            return Modals.Back() || Pages.Back();
        }
    }
}
