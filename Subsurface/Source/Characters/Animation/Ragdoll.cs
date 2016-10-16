﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;

namespace Barotrauma
{
    class Ragdoll
    {
        public static List<Ragdoll> list = new List<Ragdoll>();

        protected Hull currentHull;

        public Limb[] Limbs;
        
        private bool frozen;
        public bool Frozen
        {
            get { return frozen; }
            set 
            { 
                if (frozen == value) return;

                frozen = value;

                foreach (Limb l in Limbs)
                {
                    l.body.PhysEnabled = frozen;
                }
                collider.PhysEnabled = frozen;
            }
        }

        private Dictionary<LimbType, Limb> limbDictionary;
        public RevoluteJoint[] limbJoints;

        private bool simplePhysicsEnabled;

        private Character character;

        protected float strongestImpact;

        public float headPosition, headAngle;
        public float torsoPosition, torsoAngle;

        protected double onFloorTimer;

        private float splashSoundTimer;

        //the movement speed of the ragdoll
        public Vector2 movement;
        //the target speed towards which movement is interpolated
        protected Vector2 targetMovement;

        //a movement vector that overrides targetmovement if trying to steer
        //a Character to the position sent by server in multiplayer mode
        protected Vector2 correctionMovement;
        
        protected float floorY;
        protected float surfaceY;
        
        protected bool inWater, headInWater;
        public bool onGround;
        private bool ignorePlatforms;
        
        protected Structure stairs;
                
        protected Direction dir;

        public Direction TargetDir;

        protected PhysicsBody collider;

        public PhysicsBody Collider
        {
            get
            {
                return collider;
            }
        }

        public float FloorY
        {
            get { return floorY; }
        }

        public float Mass
        {
            get;
            private set;
        }

        public Vector2 WorldPosition
        {
            get
            {
                return character.Submarine == null ?
                    ConvertUnits.ToDisplayUnits(collider.SimPosition) :
                    ConvertUnits.ToDisplayUnits(collider.SimPosition) + character.Submarine.Position;
            }
        }

        public bool SimplePhysicsEnabled
        {
            get { return simplePhysicsEnabled; }
            set
            {
                if (value == simplePhysicsEnabled) return;

                simplePhysicsEnabled = value;

                foreach (Limb limb in Limbs)
                {
                    limb.body.Enabled = !simplePhysicsEnabled;
                }

                foreach (RevoluteJoint joint in limbJoints)
                {
                    joint.Enabled = !simplePhysicsEnabled;
                }

                if (!simplePhysicsEnabled)
                {
                    foreach (Limb limb in Limbs)
                    {
                        limb.body.SetTransform(collider.SimPosition, collider.Rotation);
                    }
                }
            }
        }

        public Vector2 TargetMovement
        {
            get 
            { 
                return (correctionMovement == Vector2.Zero) ? targetMovement : correctionMovement; 
            }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetMovement.X = MathHelper.Clamp(value.X, -5.0f, 5.0f);
                targetMovement.Y = MathHelper.Clamp(value.Y, -5.0f, 5.0f);
            }
        }

        protected virtual float HeadPosition
        { 
            get { return headPosition; } 
        }

        protected virtual float HeadAngle
        { 
            get { return headAngle; } 
        }

        protected virtual float TorsoPosition
        { 
            get { return torsoPosition; } 
        }

        protected virtual float TorsoAngle
        { 
            get { return torsoAngle; } 
        }

        public float Dir
        {
            get { return ((dir == Direction.Left) ? -1.0f : 1.0f); }
        }

        public bool InWater
        {
            get { return inWater; }
        }

        public bool HeadInWater
        {
            get { return headInWater; }
        }

        public readonly bool CanEnterSubmarine;

        public Hull CurrentHull
        {
            get { return currentHull; }
            set
            {
                if (value == currentHull) return;

                currentHull = value;
                Submarine currSubmarine = currentHull == null ? null : currentHull.Submarine;
                foreach (Limb limb in Limbs)
                {
                    limb.body.Submarine = currSubmarine;
                }
                collider.Submarine = currSubmarine;
            }
        }

        public bool IgnorePlatforms
        {
            get { return ignorePlatforms; }
            set 
            {
                if (ignorePlatforms == value) return;
                ignorePlatforms = value;

                UpdateCollisionCategories();

            }
        }

        public float StrongestImpact
        {
            get { return strongestImpact; }
            set { strongestImpact = Math.Max(value, strongestImpact); }
        }

        public Structure Stairs
        {
            get { return stairs; }
        }
        
        public Ragdoll(Character character, XElement element)
        {
            list.Add(this);

            this.character = character;

            dir = Direction.Right;

            float scale = ToolBox.GetAttributeFloat(element, "scale", 1.0f);
            
            //int limbAmount = ;
            Limbs = new Limb[element.Elements("limb").Count()];
            limbJoints = new RevoluteJoint[element.Elements("joint").Count()];
            limbDictionary = new Dictionary<LimbType, Limb>();

            headPosition = ToolBox.GetAttributeFloat(element, "headposition", 50.0f);
            headPosition = ConvertUnits.ToSimUnits(headPosition);
            headAngle = MathHelper.ToRadians(ToolBox.GetAttributeFloat(element, "headangle", 0.0f));

            torsoPosition = ToolBox.GetAttributeFloat(element, "torsoposition", 50.0f);
            torsoPosition = ConvertUnits.ToSimUnits(torsoPosition);
            torsoAngle = MathHelper.ToRadians(ToolBox.GetAttributeFloat(element, "torsoangle", 0.0f));

            CanEnterSubmarine = ToolBox.GetAttributeBool(element, "canentersubmarine", true);
                       
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "limb":
                        byte ID = Convert.ToByte(subElement.Attribute("id").Value);

                        Limb limb = new Limb(character, subElement, scale);
                        
                        limb.body.FarseerBody.OnCollision += OnLimbCollision;
                        
                        Limbs[ID] = limb;
                        Mass += limb.Mass;
                        if (!limbDictionary.ContainsKey(limb.type)) limbDictionary.Add(limb.type, limb);
                        break;
                    case "joint":
                        AddJoint(subElement, scale);

                        break;
                    case "collider":
                        collider = new PhysicsBody(subElement, scale);
                        collider.FarseerBody.FixedRotation = true;
                        break;
                }
            }

            if (collider == null)
            {
                DebugConsole.ThrowError("No collider configured for ''"+character.Name+"''!");
                collider = new PhysicsBody(0.0f, 0.0f, 0.5f, 5.0f);
                collider.BodyType = BodyType.Dynamic;
            }

            collider.CollisionCategories = Physics.CollisionCharacter;
            collider.FarseerBody.AngularDamping = 5.0f;
            collider.FarseerBody.FixedRotation = true;
            collider.FarseerBody.OnCollision += OnLimbCollision;

            UpdateCollisionCategories();

            foreach (var joint in limbJoints)
            {
                joint.BodyB.SetTransform(
                    joint.BodyA.Position + (joint.LocalAnchorA - joint.LocalAnchorB)*0.1f,
                    (joint.LowerLimit + joint.UpperLimit) / 2.0f);
            }

            float startDepth = 0.1f;
            float increment = 0.001f;

            foreach (Character otherCharacter in Character.CharacterList)
            {
                if (otherCharacter==character) continue;
                startDepth+=increment;
            }

            foreach (Limb limb in Limbs)
            {
                limb.sprite.Depth = startDepth + limb.sprite.Depth * 0.0001f;
            }
        }

        public void AddJoint(XElement subElement, float scale = 1.0f)
        {
            byte limb1ID = Convert.ToByte(subElement.Attribute("limb1").Value);
            byte limb2ID = Convert.ToByte(subElement.Attribute("limb2").Value);

            Vector2 limb1Pos = ToolBox.GetAttributeVector2(subElement, "limb1anchor", Vector2.Zero) * scale;
            limb1Pos = ConvertUnits.ToSimUnits(limb1Pos);

            Vector2 limb2Pos = ToolBox.GetAttributeVector2(subElement, "limb2anchor", Vector2.Zero) * scale;
            limb2Pos = ConvertUnits.ToSimUnits(limb2Pos);

            RevoluteJoint joint = new RevoluteJoint(Limbs[limb1ID].body.FarseerBody, Limbs[limb2ID].body.FarseerBody, limb1Pos, limb2Pos);

            joint.CollideConnected = false;

            if (subElement.Attribute("lowerlimit") != null)
            {
                joint.LimitEnabled = true;
                joint.LowerLimit = float.Parse(subElement.Attribute("lowerlimit").Value) * ((float)Math.PI / 180.0f);
                joint.UpperLimit = float.Parse(subElement.Attribute("upperlimit").Value) * ((float)Math.PI / 180.0f);
            }

            joint.MotorEnabled = true;
            joint.MaxMotorTorque = 0.25f;

            GameMain.World.AddJoint(joint);

            for (int i = 0; i < limbJoints.Length; i++)
            {
                if (limbJoints[i] != null) continue;

                limbJoints[i] = joint;
                return;
            }

            Array.Resize(ref limbJoints, limbJoints.Length + 1);
            limbJoints[limbJoints.Length - 1] = joint;

        }

        public void AddLimb(Limb limb)
        {
            limb.body.FarseerBody.OnCollision += OnLimbCollision;

            Array.Resize(ref Limbs, Limbs.Length + 1);

            Limbs[Limbs.Length-1] = limb;

            Mass += limb.Mass;
            if (!limbDictionary.ContainsKey(limb.type)) limbDictionary.Add(limb.type, limb);
        }
          
        public bool OnLimbCollision(Fixture f1, Fixture f2, Contact contact)
        {
            Structure structure = f2.Body.UserData as Structure;

            if (f2.Body.UserData is Submarine && character.Submarine == (Submarine)f2.Body.UserData) return false;
            
            //always collides with bodies other than structures
            if (structure == null)
            {
                CalculateImpact(f1, f2, contact);
                return true;
            }

            Vector2 colliderBottom = GetColliderBottom();
            
            if (structure.IsPlatform)
            {
                if (ignorePlatforms) return false;

                //the collision is ignored if the lowest limb is under the platform
                //if (lowestLimb==null || lowestLimb.Position.Y < structure.Rect.Y) return false;

                if (colliderBottom.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) return false; 
                if (f1.Body.Position.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) return false; 
                
            }
            else if (structure.StairDirection != Direction.None)
            {
                stairs = null;

                //don't collider with stairs if
                
                //1. bottom of the collider is at the bottom of the stairs and the character isn't trying to move upwards
                float stairBottomPos = ConvertUnits.ToSimUnits(structure.Rect.Y - structure.Rect.Height + 10);
                if (colliderBottom.Y < stairBottomPos && targetMovement.Y < 0.5f) return false;

                //2. bottom of the collider is at the top of the stairs and the character isn't trying to move downwards
                if (targetMovement.Y >= 0.0f && colliderBottom.Y >= ConvertUnits.ToSimUnits(structure.Rect.Y - Submarine.GridSize.Y * 5)) return false;
                               
                //3. collided with the stairs from below
                if (contact.Manifold.LocalNormal.Y < 0.0f) return false;

                //4. contact points is above the bottom half of the collider
                Vector2 normal; FarseerPhysics.Common.FixedArray2<Vector2> points;
                contact.GetWorldManifold(out normal, out points);
                if (points[0].Y > collider.SimPosition.Y) return false;
                
                //5. in water
                if (inWater && targetMovement.Y < 0.5f) return false;

                //---------------
                
                stairs = structure;
            }

            CalculateImpact(f1, f2, contact);

            return true;
        }

        private void CalculateImpact(Fixture f1, Fixture f2, Contact contact)
        {
            if (character.DisableImpactDamageTimer > 0.0f) return;

            Vector2 normal = contact.Manifold.LocalNormal;

            //Vector2 avgVelocity = Vector2.Zero;
            //foreach (Limb limb in Limbs)
            //{
            //    avgVelocity += limb.LinearVelocity;
            //}

            Vector2 velocity = f1.Body.LinearVelocity;

            if (character.Submarine == null && f2.Body.UserData is Submarine) velocity -= ((Submarine)f2.Body.UserData).Velocity;
                                    
            float impact = Vector2.Dot(velocity, -normal);

            float volume = Math.Min(impact, 1.0f);
            if (f1.Body.UserData is Limb)
            {
                Limb limb = (Limb)f1.Body.UserData;
                
                if (impact > 0.5f && limb.HitSound != null && limb.soundTimer <= 0.0f)
                {
                    limb.soundTimer = Limb.SoundInterval;
                    limb.HitSound.Play(volume, impact * 250.0f, limb.WorldPosition);
                }
            }
            else if (f1.Body == collider.FarseerBody)
            {
                if (!character.IsNetworkPlayer || GameMain.Server != null)
                {
                    character.AddDamage(CauseOfDeath.Damage, impact - 8.0f, null);
                    if (impact > 8.0f) SoundPlayer.PlayDamageSound(DamageSoundType.LimbBlunt, strongestImpact, collider);  
                    strongestImpact = Math.Max(strongestImpact, impact - 8.0f);
                }

                              

                if (Character.Controlled == character) GameMain.GameScreen.Cam.Shake = strongestImpact;
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch)
        {
            if (simplePhysicsEnabled) return;
            
            collider.UpdateDrawPosition();

            foreach (Limb limb in Limbs)
            {
                limb.Draw(spriteBatch);
            }  
        }

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            if (!GameMain.DebugDraw || !character.Enabled) return;
            if (simplePhysicsEnabled) return;

            foreach (Limb limb in Limbs)
            {

                if (limb.pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.pullJoint.WorldAnchorA);
                    if (currentHull != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Red, true, 0.01f);
                }

                limb.body.DebugDraw(spriteBatch, character.Submarine == null ? Color.Cyan : Color.White);
            }
            
            collider.DebugDraw(spriteBatch, character.Submarine == null ? Color.Cyan : Color.White);

            foreach (RevoluteJoint joint in limbJoints)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorA);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);

                pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.body.TargetPosition != Vector2.Zero)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.body.TargetPosition);
                    if (currentHull != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 10, (int)pos.Y - 10, 20, 20), Color.Cyan, false, 0.01f);
                    GUI.DrawLine(spriteBatch, pos, new Vector2(limb.WorldPosition.X, -limb.WorldPosition.Y), Color.Cyan);
                }
            }

            if (ignorePlatforms)
            {
                GUI.DrawLine(spriteBatch, 
                    new Vector2(collider.DrawPosition.X, -collider.DrawPosition.Y),
                    new Vector2(collider.DrawPosition.X, -collider.DrawPosition.Y + 50), 
                    Color.Orange, 0, 5);
            }
        }

        public virtual void Flip()
        {
            dir = (dir == Direction.Left) ? Direction.Right : Direction.Left;

            for (int i = 0; i < limbJoints.Count(); i++)
            {
                float lowerLimit = -limbJoints[i].UpperLimit;
                float upperLimit = -limbJoints[i].LowerLimit;

                limbJoints[i].LowerLimit = lowerLimit;
                limbJoints[i].UpperLimit = upperLimit;

                limbJoints[i].LocalAnchorA = new Vector2(-limbJoints[i].LocalAnchorA.X, limbJoints[i].LocalAnchorA.Y);
                limbJoints[i].LocalAnchorB = new Vector2(-limbJoints[i].LocalAnchorB.X, limbJoints[i].LocalAnchorB.Y);
            }


            for (int i = 0; i < Limbs.Count(); i++)
            {
                if (Limbs[i] == null) continue;

                Vector2 spriteOrigin = Limbs[i].sprite.Origin;
                spriteOrigin.X = Limbs[i].sprite.SourceRect.Width - spriteOrigin.X;
                Limbs[i].sprite.Origin = spriteOrigin;

                Limbs[i].Dir = Dir;

                if (Limbs[i].pullJoint == null) continue;

                Limbs[i].pullJoint.LocalAnchorA = 
                    new Vector2(
                        -Limbs[i].pullJoint.LocalAnchorA.X, 
                        Limbs[i].pullJoint.LocalAnchorA.Y);
            }            
        }

        public Vector2 GetCenterOfMass()
        {
            Vector2 centerOfMass = Vector2.Zero;
            foreach (Limb limb in Limbs)
            {
                centerOfMass += limb.Mass * limb.SimPosition;
            }

            centerOfMass /= Mass;

            return centerOfMass;
        }

        
        /// <param name="pullFromCenter">if false, force is applied to the position of pullJoint</param>
        protected void MoveLimb(Limb limb, Vector2 pos, float amount, bool pullFromCenter = false)
        {
            limb.MoveToPos(pos, amount, pullFromCenter);
        }
                
        public void ResetPullJoints()
        {
            for (int i = 0; i < Limbs.Length; i++)
            {
                if (Limbs[i] == null || Limbs[i].pullJoint == null) continue;
                Limbs[i].pullJoint.Enabled = false;
            }
        }

        public static void UpdateAll(Camera cam, float deltaTime)
        {
            foreach (Ragdoll r in list)
            {
                r.Update(cam, deltaTime);
            }
        }

        public void FindHull(Vector2? worldPosition = null, bool setSubmarine = true)
        {
            if (!CanEnterSubmarine)
            {
                return;
            }

            Vector2 findPos = worldPosition==null ? this.WorldPosition : (Vector2)worldPosition;

            Hull newHull = Hull.FindHull(findPos, currentHull);
            
            if (newHull == currentHull) return;

            if (setSubmarine)
            {
                //in -> out
                if (newHull == null && currentHull.Submarine != null)
                {
                    for (int i = -1; i < 2; i += 2)
                    {
                        //don't teleport outside the sub if right next to a hull
                        if (Hull.FindHull(findPos + new Vector2(Submarine.GridSize.X * 4.0f * i, 0.0f), currentHull) != null) return;
                        if (Hull.FindHull(findPos + new Vector2(0.0f, Submarine.GridSize.Y * 4.0f * i), currentHull) != null) return;
                    }

                    if (Gap.FindAdjacent(currentHull.ConnectedGaps, findPos, 150.0f) != null) return;

                    Teleport(ConvertUnits.ToSimUnits(currentHull.Submarine.Position), currentHull.Submarine.Velocity);
                }
                //out -> in
                else if (currentHull == null && newHull.Submarine != null)
                {
                    Teleport(-ConvertUnits.ToSimUnits(newHull.Submarine.Position), -newHull.Submarine.Velocity);
                }
                //from one sub to another
                else if (newHull != null && currentHull != null && newHull.Submarine != currentHull.Submarine)
                {
                    Teleport(ConvertUnits.ToSimUnits(currentHull.Submarine.Position - newHull.Submarine.Position),
                        Vector2.Zero);
                }
            }
            
            CurrentHull = newHull;

            character.Submarine = currentHull == null ? null : currentHull.Submarine;

            UpdateCollisionCategories();
        }
        
        public void Teleport(Vector2 moveAmount, Vector2 velocityChange)
        {
            foreach (Limb limb in Limbs)
            {
                if (limb.body.FarseerBody.ContactList == null) continue;
                
                ContactEdge ce = limb.body.FarseerBody.ContactList;
                while (ce != null && ce.Contact != null)
                {
                    ce.Contact.Enabled = false;
                    ce = ce.Next;
                }                
            }    

            foreach (Limb limb in Limbs)
            {
                limb.body.LinearVelocity += velocityChange;
            }

            //character.Stun = 0.1f;
            character.DisableImpactDamageTimer = 0.25f;

            SetPosition(collider.SimPosition + moveAmount);
            character.CursorPosition += moveAmount;
        }

        private void UpdateCollisionCategories()
        {
            Category wall = currentHull == null ? 
                Physics.CollisionLevel | Physics.CollisionWall 
                : Physics.CollisionWall;

            Category collisionCategory = (ignorePlatforms) ?
                wall | Physics.CollisionProjectile | Physics.CollisionStairs
                : wall | Physics.CollisionProjectile | Physics.CollisionPlatform | Physics.CollisionStairs;

            collider.CollidesWith = collisionCategory;

            foreach (Limb limb in Limbs)
            {
                if (limb.ignoreCollisions) continue;

                try
                {
                    limb.body.CollidesWith = collisionCategory;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to update ragdoll limb collisioncategories", e);
                }
            }
        }

        public void Update(Camera cam, float deltaTime)
        {
            if (!character.Enabled) return;

            if (Frozen) return;

            UpdateNetPlayerPosition();
            
            Vector2 flowForce = Vector2.Zero;

            FindHull();

            splashSoundTimer -= deltaTime;
            
            //ragdoll isn't in any room -> it's in the water
            if (currentHull == null)
            {
                inWater = true;
                headInWater = true;
            }
            else
            {
                flowForce = GetFlowForce();

                inWater = false;
                headInWater = false;

                var colliderB = GetColliderBottom().Y;
                float surf = ConvertUnits.ToSimUnits(currentHull.Surface);

                if (currentHull.Volume > currentHull.FullVolume * 0.95f || 
                    ConvertUnits.ToSimUnits(currentHull.Surface) - GetFloorY() > HeadPosition * 0.95f)
                    inWater = true;                
            }

            if (flowForce.LengthSquared() > 0.001f)
            {
                collider.ApplyForce(flowForce);
            }

            if (currentHull==null || 
                currentHull.Volume > currentHull.FullVolume * 0.95f || 
                ConvertUnits.ToSimUnits(currentHull.Surface) > collider.SimPosition.Y)
            {
                collider.ApplyWaterForces();
            }
            
                       
            foreach (Limb limb in Limbs)
            {
                //find the room which the limb is in
                //the room where the ragdoll is in is used as the "guess", meaning that it's checked first                
                Hull limbHull = currentHull == null ? null : Hull.FindHull(limb.WorldPosition, currentHull);
                
                bool prevInWater = limb.inWater;
                limb.inWater = false;

                if (limbHull == null)
                {                  
                    //limb isn't in any room -> it's in the water
                    limb.inWater = true;
                }
                else if (limbHull.Volume > 0.0f && Submarine.RectContains(limbHull.Rect, limb.Position))
                {
                    if (limb.Position.Y < limbHull.Surface)                        
                    {
                        limb.inWater = true;

                        if (flowForce.LengthSquared() > 0.001f)
                        {
                            limb.body.ApplyForce(flowForce);
                        }

                        surfaceY = limbHull.Surface;

                        if (limb.type == LimbType.Head)
                        {
                            headInWater = true;
                        }
                    }
                        //the limb has gone through the surface of the water
                    if (Math.Abs(limb.LinearVelocity.Y) > 5.0f && limb.inWater != prevInWater)
                    {

                        //create a splash particle
                        var p = GameMain.ParticleManager.CreateParticle("watersplash",
                            new Vector2(limb.Position.X, limbHull.Surface) + limbHull.Submarine.Position,
                            new Vector2(0.0f, Math.Abs(-limb.LinearVelocity.Y * 20.0f)),
                            0.0f, limbHull);
                                                
                        GameMain.ParticleManager.CreateParticle("bubbles",
                            new Vector2(limb.Position.X, limbHull.Surface) + limbHull.Submarine.Position,                            
                            limb.LinearVelocity*0.001f,
                            0.0f, limbHull);

                        //if the Character dropped into water, create a wave
                        if (limb.LinearVelocity.Y<0.0f)
                        {
                            if (splashSoundTimer <= 0.0f)
                            {
                                SoundPlayer.PlaySplashSound(limb.WorldPosition, Math.Abs(limb.LinearVelocity.Y) + Rand.Range(-5.0f, 0.0f));
                                splashSoundTimer = 0.5f;
                            }

                            //1.0 when the limb is parallel to the surface of the water
                            // = big splash and a large impact
                            float parallel = (float)Math.Abs(Math.Sin(limb.Rotation));
                            Vector2 impulse = Vector2.Multiply(limb.LinearVelocity, -parallel * limb.Mass);
                            //limb.body.ApplyLinearImpulse(impulse);
                            int n = (int)((limb.Position.X - limbHull.Rect.X) / Hull.WaveWidth);
                            limbHull.WaveVel[n] = Math.Min(impulse.Y * 1.0f, 5.0f);
                            StrongestImpact = ((impulse.Length() * 0.5f) - limb.impactTolerance);
                        }
                    }
                }

                limb.Update(deltaTime);
            }


            bool onStairs = stairs != null;
            stairs = null;

            var contacts = collider.FarseerBody.ContactList;
            while (collider.FarseerBody.Enabled && contacts != null && contacts.Contact != null)
            {
                if (contacts.Contact.Enabled && contacts.Contact.IsTouching)
                {
                    Vector2 normal;
                    FarseerPhysics.Common.FixedArray2<Vector2> points;

                    contacts.Contact.GetWorldManifold(out normal, out points);

                    switch (contacts.Contact.FixtureA.CollisionCategories)
                    {
                        case Physics.CollisionStairs:
                            Structure structure = contacts.Contact.FixtureA.Body.UserData as Structure;
                            if (structure != null && onStairs)
                            {
                                stairs = structure;
                            }
                            break;
                    }
                    //    case Physics.CollisionPlatform:
                    //        Structure platform = contacts.Contact.FixtureA.Body.UserData as Structure;
                    //        if (IgnorePlatforms || colliderBottom.Y < ConvertUnits.ToSimUnits(platform.Rect.Y - 15))
                    //        {
                    //            contacts = contacts.Next;
                    //            continue;
                    //        }
                    //        break;
                    //    case Physics.CollisionWall:
                    //        break;
                    //    default:
                    //            contacts = contacts.Next;
                    //            continue;
                    //}


                    if (points[0].Y < collider.SimPosition.Y)
                    {
                        floorY = Math.Max(floorY, points[0].Y);

                        onGround = true;
                        onFloorTimer = 0.1f;
                    }


                }

                contacts = contacts.Next;
            }

            //the ragdoll "stays on ground" for 50 millisecs after separation
            if (onFloorTimer <= 0.0f)
            {
                onGround = false;
            }
            else
            {
                onFloorTimer -= deltaTime;
            }


        }

        private float GetFloorY()
        {
            Vector2 rayStart = collider.SimPosition; 
            Vector2 rayEnd = rayStart - new Vector2(0.0f, TorsoPosition);

            var lowestLimb = FindLowestLimb();

            float closestFraction = 1;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                switch (fixture.CollisionCategories)
                {
                    case Physics.CollisionStairs:
                        if (inWater && TargetMovement.Y < 0.5f) return -1;
                        //Structure structure = fixture.Body.UserData as Structure;
                        //if (stairs == null && structure != null)
                        //{
                        //    if (LowestLimb.SimPosition.Y < structure.SimPosition.Y)
                        //    {
                        //        return -1;
                        //    }
                        //    else
                        //    {
                        //        stairs = structure;
                        //    }
                        //}
                        break;
                    case Physics.CollisionPlatform:
                        Structure platform = fixture.Body.UserData as Structure;
                        if (IgnorePlatforms || lowestLimb.Position.Y < platform.Rect.Y) return -1;
                        break;
                    case Physics.CollisionWall:
                        break;
                    default:
                        return -1;
                }

                if (fraction < closestFraction)
                {
                    closestFraction = fraction;
                }

                return closestFraction;
            } 
            , rayStart, rayEnd);


            if (closestFraction == 1) //raycast didn't hit anything
            {
                return (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
            }
            else
            {
                return rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
            }
            
        }

        public void SetPosition(Vector2 simPosition, bool lerp = false)
        {
            Vector2 moveAmount = simPosition - collider.SimPosition;

            collider.SetTransform(simPosition, collider.Rotation);

            foreach (Limb limb in Limbs)
            {
                //check visibility from the new position of the collider to the new position of this limb
                Vector2 movePos = limb.SimPosition + moveAmount;

                TrySetLimbPosition(limb, simPosition, movePos, lerp);
            }
        }

        protected void TrySetLimbPosition(Limb limb, Vector2 original, Vector2 simPosition, bool lerp = false)
        {
            Vector2 movePos = simPosition;

            if (original != simPosition)
            {
                Category collisionCategory = Physics.CollisionWall | Physics.CollisionLevel;
                //if (!ignorePlatforms) collisionCategory |= Physics.CollisionPlatform;

                Body body = Submarine.PickBody(original, simPosition, null, collisionCategory);
            
                //if there's something in between the limbs
                if (body != null)
                {
                    //move the limb close to the position where the raycast hit something
                    movePos = original + ((simPosition - original) * Submarine.LastPickedFraction * 0.9f);
                }
            }

            if (lerp)
            {
                limb.body.TargetPosition = movePos;
                limb.body.MoveToTargetPosition(Vector2.Distance(limb.SimPosition, movePos) < 10.0f);                
            }
            else
            {
                limb.body.SetTransform(movePos, limb.Rotation);
                if (limb.pullJoint != null)
                {
                    limb.pullJoint.WorldAnchorB = limb.pullJoint.WorldAnchorA;
                    limb.pullJoint.Enabled = false;
                }              
            }
        }

        //public void SetRotation(float rotation)
        //{
        //    float rotateAmount = rotation - refLimb.Rotation;

        //    Matrix rotationMatrix = Matrix.CreateRotationZ(rotateAmount);

        //    refLimb.body.SetTransform(refLimb.SimPosition, rotation);

        //    foreach (Limb limb in Limbs)
        //    {
        //        if (limb == refLimb) continue;

        //        Vector2 newPos = limb.SimPosition - refLimb.SimPosition;
        //        newPos = Vector2.Transform(newPos, rotationMatrix);

        //        TrySetLimbPosition(limb, refLimb.SimPosition, refLimb.SimPosition + newPos);
        //        limb.body.SetTransform(limb.SimPosition, limb.Rotation + rotateAmount);
        //    }
        //}

        private void UpdateNetPlayerPosition()
        {
            //if (refLimb.body.TargetPosition == Vector2.Zero)
            //{
            //    correctionMovement = Vector2.Zero;
            //    return;
            //}

            ////if the limb is closer than alloweddistance, just ignore the difference
            //float allowedDistance = NetConfig.AllowedRagdollDistance * ((inWater) ? 2.0f : 1.0f);

            //if (currentHull == null)
            //{
            //    var overLappingHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(refLimb.body.TargetPosition), null, true);

            //    if (overLappingHull != null)
            //    {
            //        Submarine.PickBody(refLimb.SimPosition, refLimb.body.TargetPosition, null, Physics.CollisionWall);

            //        refLimb.body.TargetPosition = refLimb.SimPosition + (refLimb.body.TargetPosition - refLimb.SimPosition) * Submarine.LastPickedFraction * 0.9f;
            //    }
            //}


            //float dist = Vector2.Distance(refLimb.body.SimPosition, refLimb.body.TargetPosition);
            
            ////if the limb is further away than resetdistance, all limbs are immediately snapped to their targetpositions
            //bool resetAll = dist > NetConfig.ResetRagdollDistance;

            //Vector2 diff = (refLimb.body.TargetPosition - refLimb.body.SimPosition);

            //if (diff == Vector2.Zero || diff.Length() < allowedDistance)
            //{
            //    refLimb.body.TargetPosition = Vector2.Zero;
            //    foreach (Limb limb in Limbs)
            //    {
            //        limb.body.TargetPosition = Vector2.Zero;
            //    }

            //    correctionMovement = Vector2.Zero;
            //    return;
            //}
          
            //if (resetAll)
            //{
            //    System.Diagnostics.Debug.WriteLine("reset ragdoll limb positions");

            //    SetPosition(refLimb.body.TargetPosition, dist < 10.0f);

            //    return;
            //}

            //if (inWater)
            //{
            //    if (targetMovement.LengthSquared() > 0.01f)
            //    {
            //        correctionMovement =
            //            Vector2.Lerp(targetMovement, Vector2.Normalize(diff) * MathHelper.Clamp(dist * 5.0f, 0.1f, 5.0f), 0.2f);

            //    }
            //    else
            //    {
            //        refLimb.body.LinearVelocity = Vector2.Lerp(
            //            refLimb.LinearVelocity,
            //            Vector2.Normalize(diff) * MathHelper.Clamp(dist, 0.0f, 5.0f),
            //            0.2f);
            //    }
            //}
            //else
            //{
            //    //clamp the magnitude of the correction movement between 0.5f - 5.0f
            //    Vector2 newCorrectionMovement = Vector2.Normalize(diff) * MathHelper.Clamp(dist * 2.0f, 0.5f, 5.0f);

            //    //heading in the right direction -> use the \"normal\" movement if it's faster than correctionMovement
            //    //i.e. the character is close to the targetposition but the character is still running
            //    if (Math.Sign(targetMovement.X) == Math.Sign(newCorrectionMovement.X))
            //    {
            //        newCorrectionMovement.X = Math.Max(Math.Abs(targetMovement.X), Math.Abs(newCorrectionMovement.X)) * Math.Sign(targetMovement.X);
            //    }

            //    correctionMovement = Vector2.Lerp(correctionMovement, newCorrectionMovement, 0.5f);

            //    if (Math.Abs(correctionMovement.Y) < 0.1f) correctionMovement.Y = 0.0f;
            //}            
        }

        public virtual Vector2 EstimateCurrPosition(Vector2 prevPosition, float timePassed)
        {
            return prevPosition;
        }


        private Vector2 GetFlowForce()
        {
            Vector2 limbPos = ConvertUnits.ToDisplayUnits(Limbs[0].SimPosition);

            Vector2 force = Vector2.Zero;
            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                Gap gap = e as Gap;
                if (gap == null || gap.FlowTargetHull != currentHull || gap.LerpedFlowForce == Vector2.Zero) continue;

                Vector2 gapPos = gap.SimPosition;

                float dist = Vector2.Distance(limbPos, gapPos);

                force += Vector2.Normalize(gap.LerpedFlowForce) * (Math.Max(gap.LerpedFlowForce.Length() - dist, 0.0f) / 500.0f);
            }

            if (force.Length() > 20.0f) return force;
            return force;
        }

        public Limb GetLimb(LimbType limbType)
        {
            Limb limb = null;
            limbDictionary.TryGetValue(limbType, out limb);
            return limb;
        }


        public Vector2 GetColliderBottom()
        {
            return  collider.SimPosition - Vector2.UnitY * (collider.height / 2 + collider.radius);
        }

        public Limb FindLowestLimb()
        {
            Limb lowestLimb = null;
            foreach (Limb limb in Limbs)
            {
                if (lowestLimb == null)
                    lowestLimb = limb;
                else if (limb.SimPosition.Y < lowestLimb.SimPosition.Y)
                    lowestLimb = limb;
            }

            return lowestLimb;
        }

        public void Remove()
        {
            foreach (Limb l in Limbs) l.Remove();
            foreach (RevoluteJoint joint in limbJoints)
            {
                GameMain.World.RemoveJoint(joint);
            }
        }

    }
}
