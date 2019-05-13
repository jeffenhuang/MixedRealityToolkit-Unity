﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Extensions.Experimental.Socketer;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Extensions.Experimental.SpectatorView
{
    internal abstract class SynchronizedRenderer<TRenderer, TComponentService> : SynchronizedComponent<TComponentService, byte>
        where TRenderer : Renderer
        where TComponentService : Singleton<TComponentService>, ISynchronizedComponentService
    {
        public static class ChangeType
        {
            public const byte None = 0x0;
            public const byte Enabled = 0x1;
            public const byte Materials = 0x2;
            public const byte MaterialProperty = 0x4;
        }

        private bool previousEnabled;
        private SynchronizedMaterials synchronizedMaterials = new SynchronizedMaterials();

        public TRenderer Renderer
        {
            get; private set;
        }

        protected abstract byte InitialChangeType
        {
            get;
        }

        protected virtual bool IsRendererEnabled
        {
            get { return Renderer.enabled; }
        }

        protected override void OnInitialized()
        {
            Renderer = GetComponent<TRenderer>();
        }

        protected override bool HasChanges(byte changeFlags)
        {
            return changeFlags != 0;
        }

        protected override byte CalculateDeltaChanges()
        {
            byte changeType = ChangeType.None;
            if (previousEnabled != IsRendererEnabled)
            {
                previousEnabled = IsRendererEnabled;
                changeType |= ChangeType.Enabled;
            }

            bool areMaterialsDifferent;
            synchronizedMaterials.UpdateMaterials(Renderer, SynchronizedTransform.PerformanceParameters, Renderer.sharedMaterials, out areMaterialsDifferent);
            if (areMaterialsDifferent)
            {
                changeType |= ChangeType.Materials;
            }

            if (!HasFlag(changeType, ChangeType.Materials))
            {
                changeType |= ChangeType.MaterialProperty;
            }

            return changeType;
        }

        protected override void SendCompleteChanges(IEnumerable<SocketEndpoint> endpoints)
        {
            SendDeltaChanges(endpoints, InitialChangeType);
        }

        protected override void SendDeltaChanges(IEnumerable<SocketEndpoint> endpoints, byte changeFlags)
        {
            byte changeFlagsWithoutMaterialProperty = (byte)(changeFlags & ~ChangeType.MaterialProperty);
            if (changeFlags != ChangeType.MaterialProperty)
            {
                using (MemoryStream memoryStream = new MemoryStream())
                using (BinaryWriter message = new BinaryWriter(memoryStream))
                {
                    SynchronizedComponentService.WriteHeader(message, this);
                    message.Write(changeFlagsWithoutMaterialProperty);

                    WriteRenderer(message, changeFlagsWithoutMaterialProperty);

                    message.Flush();
                    SynchronizedSceneManager.Instance.Send(endpoints, memoryStream.ToArray());
                }
            }

            if (HasFlag(changeFlags, ChangeType.MaterialProperty))
            {
                synchronizedMaterials.SendMaterialPropertyChanges(endpoints, Renderer, SynchronizedTransform.PerformanceParameters, message =>
                {
                    SynchronizedComponentService.WriteHeader(message, this);
                    message.Write(ChangeType.MaterialProperty);
                }, ShouldSynchronizeMaterialProperty);
            }
        }

        protected virtual bool ShouldSynchronizeMaterialProperty(MaterialPropertyAsset materialProperty)
        {
            return true;
        }

        public static bool HasFlag(byte changeType, byte flag)
        {
            return (changeType & flag) == flag;
        }

        protected virtual void WriteRenderer(BinaryWriter message, byte changeType)
        {
            if (HasFlag(changeType, ChangeType.Enabled))
            {
                message.Write(IsRendererEnabled);
            }
            if (HasFlag(changeType, ChangeType.Materials))
            {
                synchronizedMaterials.SendMaterials(message, Renderer, ShouldSynchronizeMaterialProperty);
            }
        }
    }
}