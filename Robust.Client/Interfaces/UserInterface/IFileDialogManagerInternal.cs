using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Robust.Client.Interfaces.UserInterface
{
    /// <summary>
    ///     Manager for opening of file dialogs.
    /// </remarks>
    internal interface IFileDialogManagerInternal : IFileDialogManager
    {
        /// <summary>
        ///     Open a file dialog used for opening a single file.
        /// </summary>
        /// <returns>
        /// The filename the user chose to open.
        /// <see langword="null" /> if the user cancelled the action.
        /// </returns>
        Task<string?> GetOpenFileName(FileDialogFilters? filters = null);

        /// <summary>
        ///     Open a file dialog used for saving a single file.
        /// </summary>
        /// <returns>
        /// The filename the user chose to save to.
        /// <see langword="null" /> if the user cancelled the action.
        /// </returns>
        Task<string?> GetSaveFileName();
    }
}
