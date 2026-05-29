// <copyright file="CommonGameSystemBase.cs" company="Luca Rager">
// Copyright (c) Luca Rager. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace LucaModsCommon.Systems {
    #region Using Statements

    using Game;
    using LucaModsCommon.Utils;

    #endregion

    /// <summary>
    /// Base <see cref="GameSystemBase"/> that wires up a <see cref="PrefixedLogger"/> in OnCreate,
    /// prefixed with the derived system's type name. Derived systems should call
    /// <c>base.OnCreate()</c> when overriding.
    /// </summary>
    public abstract partial class CommonGameSystemBase : GameSystemBase {
        protected internal PrefixedLogger m_Log;

        /// <inheritdoc/>
        protected override void OnCreate() {
            base.OnCreate();
            m_Log = new PrefixedLogger(GetType().Name);
            m_Log.Debug("OnCreate()");
        }
    }
}
