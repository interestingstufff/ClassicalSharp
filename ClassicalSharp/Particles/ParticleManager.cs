﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using System;
using ClassicalSharp.GraphicsAPI;
using ClassicalSharp.Textures;
using OpenTK;
using BlockID = System.UInt16;
using TexLoc = System.UInt16;

namespace ClassicalSharp.Particles {
	
	public sealed class ParticleManager : IGameComponent {
		
		public int ParticlesTexId;
		TerrainParticle[] terrainParticles = new TerrainParticle[maxParticles];
		RainParticle[] rainParticles = new RainParticle[maxParticles];
		int terrainCount, rainCount;
		int[] terrain1DCount   = new int[Atlas1D.MaxAtlases];
		int[] terrain1DIndices = new int[Atlas1D.MaxAtlases];
		
		Game game;
		Random rnd = new Random();
		int vb;
		const int maxParticles = 600;
		
		void IGameComponent.Init(Game game) {
			this.game = game;
			Events.BlockChanged += BreakBlockEffect;
			Events.TextureChanged += TextureChanged;
			
			ContextRecreated();
			Events.ContextLost += ContextLost;
			Events.ContextRecreated += ContextRecreated;
		}
		
		void IGameComponent.Ready(Game game) { }
		void IGameComponent.Reset(Game game) { rainCount = 0; terrainCount = 0; }
		void IGameComponent.OnNewMap(Game game) { rainCount = 0; terrainCount = 0; }
		void IGameComponent.OnNewMapLoaded(Game game) { }
		
		void TextureChanged(string name, byte[] data) {
			if (Utils.CaselessEq(name, "particles.png")) {
				game.LoadTexture(ref ParticlesTexId, name, data);
			}
		}
		
		
		VertexP3fT2fC4b[] vertices = new VertexP3fT2fC4b[0];
		public void Render(double delta, float t) {
			if (terrainCount == 0 && rainCount == 0) return;
			if (game.Graphics.LostContext) return;
			
			IGraphicsApi gfx = game.Graphics;
			gfx.Texturing = true;
			gfx.AlphaTest = true;
			gfx.SetBatchFormat(VertexFormat.P3fT2fC4b);
			
			RenderTerrainParticles(gfx, terrainParticles, terrainCount, delta, t);
			RenderRainParticles(gfx, rainParticles, rainCount, delta, t);
			
			gfx.AlphaTest = false;
			gfx.Texturing = false;
		}
		
		unsafe void RenderTerrainParticles(IGraphicsApi gfx, TerrainParticle[] particles, int elems, double delta, float t) {
			int count = elems * 4;
			if (count > vertices.Length)
				vertices = new VertexP3fT2fC4b[count];

			Update1DCounts(particles, elems);
			for (int i = 0; i < elems; i++) {
				int index = Atlas1D.Get1DIndex(particles[i].texLoc);
				particles[i].Render(game, t, vertices, ref terrain1DIndices[index]);
			}
			int drawCount = Math.Min(count, maxParticles * 4);
			if (drawCount == 0) return;
			
			fixed (VertexP3fT2fC4b* ptr = vertices) {
				gfx.SetDynamicVbData(vb, (IntPtr)ptr, drawCount);
				int offset = 0;
				for (int i = 0; i < Atlas1D.AtlasesCount; i++) {
					int partCount = terrain1DCount[i];
					if (partCount == 0) continue;
					
					gfx.BindTexture(Atlas1D.TexIds[i]);
					gfx.DrawVb_IndexedTris(partCount, offset);
					offset += partCount;
				}
			}
		}
		
		void Update1DCounts(TerrainParticle[] particles, int elems) {
			for (int i = 0; i < Atlas1D.MaxAtlases; i++) {
				terrain1DCount[i] = 0;
				terrain1DIndices[i] = 0;
			}
			for (int i = 0; i < elems; i++) {
				int index = Atlas1D.Get1DIndex(particles[i].texLoc);
				terrain1DCount[index] += 4;
			}
			for (int i = 1; i < Atlas1D.AtlasesCount; i++) {
				terrain1DIndices[i] = terrain1DIndices[i - 1] + terrain1DCount[i - 1];
			}
		}
		
		void RenderRainParticles(IGraphicsApi gfx, RainParticle[] particles, int elems, double delta, float t) {
			int count = elems * 4;
			if (count > vertices.Length)
				vertices = new VertexP3fT2fC4b[count];
			
			int index = 0;
			for (int i = 0; i < elems; i++)
				particles[i].Render(game, t, vertices, ref index);
			
			int drawCount = Math.Min(count, maxParticles * 4);
			if (drawCount == 0) return;
			gfx.BindTexture(ParticlesTexId);
			gfx.UpdateDynamicVb_IndexedTris(vb, vertices, drawCount);
		}
		
		
		public void Tick(ScheduledTask task) {
			TickTerrainParticles(task.Interval);
			TickRainParticles(task.Interval);
		}
		
		void TickTerrainParticles(double delta) {
			for (int i = 0; i < terrainCount; i++) {
				if (terrainParticles[i].Tick(game, delta)) {
					RemoveTerrainAt(i); i--;
				}
			}
		}
		
		void RemoveTerrainAt(int index) {
			TerrainParticle removed = terrainParticles[index];
			for (int i = index; i < terrainCount - 1; i++) {
				terrainParticles[i] = terrainParticles[i + 1];
			}
			terrainParticles[terrainCount - 1] = removed;
			terrainCount--;
		}
		
		void TickRainParticles(double delta) {
			for (int i = 0; i < rainCount; i++) {
				if (rainParticles[i].Tick(game, delta)) {
					RemoveRainAt(i); i--;
				}
			}
		}
		
		void RemoveRainAt(int index) {
			RainParticle removed = rainParticles[index];
			for (int i = index; i < rainCount - 1; i++) {
				rainParticles[i] = rainParticles[i + 1];
			}
			rainParticles[rainCount - 1] = removed;
			rainCount--;
		}		
		
		
		void BreakBlockEffect(Vector3I coords, BlockID old, BlockID now) {
			if (now != Block.Air || BlockInfo.Draw[old] == DrawType.Gas) return;
			
			Vector3 worldPos = new Vector3(coords.X, coords.Y, coords.Z);
			int texLoc = BlockInfo.GetTextureLoc(old, Side.Left), texIndex = 0;
			TextureRec baseRec = Atlas1D.GetTexRec(texLoc, 1, out texIndex);
			float uScale = (1/16f), vScale = (1/16f) * Atlas1D.invTileSize;
			
			Vector3 minBB = BlockInfo.MinBB[old];
			Vector3 maxBB = BlockInfo.MaxBB[old];
			int minU = Math.Min((int)(minBB.X * 16), (int)(minBB.Z * 16));
			int maxU = Math.Min((int)(maxBB.X * 16), (int)(maxBB.Z * 16));
			int minV = (int)(16 - maxBB.Y * 16), maxV = (int)(16 - minBB.Y * 16);
			int maxUsedU = maxU, maxUsedV = maxV;
			// This way we can avoid creating particles which outside the bounds and need to be clamped
			if (minU < 12 && maxU > 12) maxUsedU = 12;
			if (minV < 12 && maxV > 12) maxUsedV = 12;
			
			const int gridSize = 4;
			// gridOffset gives the centre of the cell on a grid
			const float cellCentre = (1f / gridSize) * 0.5f;
			
			for (int x = 0; x < gridSize; x++)
				for (int y = 0; y < gridSize; y++)
					for (int z = 0; z < gridSize; z++)
			{
				float cellX = (float)x / gridSize, cellY = (float)y / gridSize, cellZ = (float)z / gridSize;
				Vector3 cell = new Vector3(cellCentre + cellX, cellCentre / 2 + cellY, cellCentre + cellZ);
				if (cell.X < minBB.X || cell.X > maxBB.X || cell.Y < minBB.Y
				    || cell.Y > maxBB.Y || cell.Z < minBB.Z || cell.Z > maxBB.Z) continue;
				
				double velX = cellCentre + (cellX - 0.5f) + (rnd.NextDouble() * 0.4 - 0.2); // centre random offset around [-0.2, 0.2]
				double velY = cellCentre + (cellY - 0.0f) + (rnd.NextDouble() * 0.4 - 0.2);
				double velZ = cellCentre + (cellZ - 0.5f) + (rnd.NextDouble() * 0.4 - 0.2);
				Vector3 velocity = new Vector3((float)velX, (float)velY, (float)velZ);
				
				TextureRec rec = baseRec;
				rec.U1 = baseRec.U1 + rnd.Next(minU, maxUsedU) * uScale;
				rec.V1 = baseRec.V1 + rnd.Next(minV, maxUsedV) * vScale;
				rec.U2 = Math.Min(baseRec.U1 + maxU * uScale, rec.U1 + 4 * uScale) - 0.01f * uScale;
				rec.V2 = Math.Min(baseRec.V1 + maxV * vScale, rec.V1 + 4 * vScale) - 0.01f * vScale;
				double life = 0.3 + rnd.NextDouble() * 1.2;
				
				TerrainParticle p = GetTerrainParticle();
				p.ResetState(worldPos + cell, velocity, life);
				p.rec = rec;
				
				p.texLoc = (TexLoc)texLoc;
				p.block = old;
				int type = rnd.Next(0, 30);
				p.Size = (byte)(type >= 28 ? 12 : (type >= 25 ? 10 : 8));
			}
		}
		
		TerrainParticle GetTerrainParticle() {
			if (terrainCount == maxParticles) RemoveTerrainAt(0);
			terrainCount++;
			
			TerrainParticle particle = terrainParticles[terrainCount - 1];
			if (particle != null) return particle;
			
			particle = new TerrainParticle();
			terrainParticles[terrainCount - 1] = particle;
			return particle;
		}
		
		public void AddRainParticle(Vector3 pos) {
			Vector3 startPos = pos;
			for (int i = 0; i < 2; i++) {
				double velX = rnd.NextDouble() * 0.8 - 0.4; // [-0.4, 0.4]
				double velZ = rnd.NextDouble() * 0.8 - 0.4;
				double velY = rnd.NextDouble() + 0.4;
				Vector3 velocity = new Vector3((float)velX, (float)velY, (float)velZ);
				
				double xOffset = rnd.NextDouble() - 0.5; // [-0.5, 0.5]
				double yOffset = rnd.NextDouble() * 0.1 + 0.01;
				double zOffset = rnd.NextDouble() - 0.5;
				pos = startPos + new Vector3(0.5f + (float)xOffset,
				                             (float)yOffset, 0.5f + (float)zOffset);
				double life = 40;
				RainParticle p = GetRainParticle();
				p.ResetState(pos, velocity, life);
				int type = rnd.Next(0, 30);
				p.Size = (byte)(type >= 28 ? 2 : (type >= 25 ? 4 : 3));
			}
		}
		
		RainParticle GetRainParticle() {
			if (rainCount == maxParticles) RemoveRainAt(0);
			rainCount++;
			
			RainParticle particle = rainParticles[rainCount - 1];
			if (particle != null) return particle;
			
			particle = new RainParticle();
			rainParticles[rainCount - 1] = particle;
			return particle;
		}
		
		
		void IDisposable.Dispose() {
			game.Graphics.DeleteTexture(ref ParticlesTexId);
			Events.BlockChanged -= BreakBlockEffect;
			Events.TextureChanged -= TextureChanged;
			
			ContextLost();
			Events.ContextLost -= ContextLost;
			Events.ContextRecreated -= ContextRecreated;
		}
		
		void ContextLost() { game.Graphics.DeleteVb(ref vb); }
		
		void ContextRecreated() {
			vb = game.Graphics.CreateDynamicVb(VertexFormat.P3fT2fC4b, maxParticles * 4);
		}
	}
}
