// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml;

namespace ClassicUO.Game.UI.Gumps
{
    internal class Gump : Control
    {
        public Gump(World world, uint local, uint server)
        {
            World = world;
            LocalSerial = local;
            ServerSerial = server;
            AcceptMouseInput = false;
            AcceptKeyboardInput = false;
        }

        public World World { get; }

        public bool CanBeSaved => GumpType != Gumps.GumpType.None;

        public virtual GumpType GumpType { get; }

        public bool InvalidateContents { get; set; }

        public uint MasterGumpSerial { get; set; }


        public override void Update()
        {
            if (InvalidateContents)
            {
                UpdateContents();
                InvalidateContents = false;
            }

            if (ActivePage == 0)
            {
                ActivePage = 1;
            }

            base.Update();
        }

        public override void Dispose()
        {
            Item it = World.Items.Get(LocalSerial);

            if (it != null && it.Opened)
            {
                it.Opened = false;
            }

            base.Dispose();
        }


        public virtual void Save(XmlTextWriter writer)
        {
            writer.WriteAttributeString("type", ((int) GumpType).ToString());
            writer.WriteAttributeString("x", X.ToString());
            writer.WriteAttributeString("y", Y.ToString());
            writer.WriteAttributeString("serial", LocalSerial.ToString());
        }

        public void SetInScreen()
        {
            Rectangle windowBounds = Client.Game.ClientBounds;
            Rectangle bounds = Bounds;
            bounds.X += windowBounds.X;
            bounds.Y += windowBounds.Y;

            if (windowBounds.Intersects(bounds))
            {
                return;
            }

            X = 0;
            Y = 0;
        }

        public virtual void Restore(XmlElement xml)
        {
        }

        public void RequestUpdateContents()
        {
            InvalidateContents = true;
        }

        protected virtual void UpdateContents()
        {
        }

        protected override void OnDragEnd(int x, int y)
        {
            Point position = Location;
            int halfWidth = Width - (Width >> 2);
            int halfHeight = Height - (Height >> 2);

            if (X < -halfWidth)
            {
                position.X = -halfWidth;
            }

            if (Y < -halfHeight)
            {
                position.Y = -halfHeight;
            }

            if (X > Client.Game.ClientBounds.Width - (Width - halfWidth))
            {
                position.X = Client.Game.ClientBounds.Width - (Width - halfWidth);
            }

            if (Y > Client.Game.ClientBounds.Height - (Height - halfHeight))
            {
                position.Y = Client.Game.ClientBounds.Height - (Height - halfHeight);
            }

            Location = position;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            return IsVisible && base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
        }

        // ───── Retained-mode render cache ─────
        //
        // When EnableRenderCache is true, the gump's emitted GumpDrawCommand
        // stream is cached between frames. The cache is replayed into the
        // per-frame unified RenderLists on subsequent frames as long as nothing
        // in the gump has changed. A change bumps _renderVersion, which causes
        // the next EmitCommandsInto call to rebuild the cache from scratch.
        //
        // Commit 4 lays the infrastructure but leaves EnableRenderCache default-
        // off so behaviour is identical to pre-cache. Commit 5 wires render-
        // affecting Control setters to call InvalidateRenderCache; Commit 6
        // adds a pure-translation fast path for dragged gumps.
        //
        // Gumps that emit any GumpCommandKind.Callback commands can still be
        // cached because the Func reference is stored in the struct; the
        // closure captures its own state. Cache invalidation is still the
        // owning gump's responsibility when that captured state would change.

        private List<GumpDrawCommand> _renderCache;
        private int _renderVersion;
        private int _lastBuiltRenderVersion = -1;

        /// <summary>
        /// Opt-in flag for the retained-mode command cache. When true, the
        /// gump's emitted command stream is snapshotted and replayed unchanged
        /// on subsequent frames until <see cref="InvalidateRenderCache"/> fires.
        /// Default false to preserve pre-cache behaviour until callers are
        /// audited for render-affecting setters.
        /// </summary>
        public bool EnableRenderCache { get; set; }

        /// <summary>
        /// Bump the render version, forcing the next frame to rebuild the
        /// cached command stream. Safe to call every frame; the rebuild only
        /// actually runs on the next <see cref="EmitCommandsInto"/> call.
        /// </summary>
        internal void InvalidateRenderCache()
        {
            _renderVersion++;
        }

        /// <summary>
        /// Emit this gump's draw commands into the supplied <see cref="RenderLists"/>.
        /// If <see cref="EnableRenderCache"/> is true and the cache is current,
        /// replays the cached command stream without re-walking the control tree.
        /// Otherwise rebuilds the cache by calling <see cref="AddToRenderLists"/>.
        /// </summary>
        internal void EmitCommandsInto(RenderLists target, int gumpX, int gumpY, ref float layerDepth)
        {
            if (!EnableRenderCache)
            {
                AddToRenderLists(target, gumpX, gumpY, ref layerDepth);
                return;
            }

            if (_renderCache == null || _renderVersion != _lastBuiltRenderVersion)
            {
                // Rebuild: emit directly into target and snapshot the range of
                // commands we just produced.
                int startIndex = target.GumpCommandCount;
                AddToRenderLists(target, gumpX, gumpY, ref layerDepth);
                int endIndex = target.GumpCommandCount;

                _renderCache ??= new List<GumpDrawCommand>(endIndex - startIndex);
                _renderCache.Clear();
                for (int i = startIndex; i < endIndex; i++)
                {
                    _renderCache.Add(target.PeekGumpCommand(i));
                }

                _lastBuiltRenderVersion = _renderVersion;
            }
            else
            {
                // Cache hit: replay the cached commands into target.
                target.AppendCommands(_renderCache);
            }
        }

        public override void OnButtonClick(int buttonID)
        {
            if (!IsDisposed && LocalSerial != 0)
            {
                List<uint> switches = new List<uint>();
                List<Tuple<ushort, string>> entries = new List<Tuple<ushort, string>>();

                foreach (Control control in Children)
                {
                    switch (control)
                    {
                        case Checkbox checkbox when checkbox.IsChecked:
                            switches.Add(control.LocalSerial);

                            break;

                        case StbTextBox textBox:
                            entries.Add(new Tuple<ushort, string>((ushort) textBox.LocalSerial, textBox.Text));

                            break;
                    }
                }

                GameActions.ReplyGump
                (
                    LocalSerial,
                    // Seems like MasterGump serial does not work as expected.
                    /*MasterGumpSerial != 0 ? MasterGumpSerial :*/ ServerSerial,
                    buttonID,
                    switches.ToArray(),
                    entries.ToArray()
                );

                if (CanMove)
                {
                    UIManager.SavePosition(ServerSerial, Location);
                }
                else
                {
                    UIManager.RemovePosition(ServerSerial);
                }

                Dispose();
            }
        }

        protected override void CloseWithRightClick()
        {
            if (!CanCloseWithRightClick)
            {
                return;
            }

            if (ServerSerial != 0)
            {
                OnButtonClick(0);
            }

            base.CloseWithRightClick();
        }

        public override void ChangePage(int pageIndex)
        {
            // For a gump, Page is the page that is drawing.
            ActivePage = pageIndex;
        }
    }
}