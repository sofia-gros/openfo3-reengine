using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenFo3.Game
{
    public enum CraftingStationType
    {
        Workbench,
        ChemLab,
        ReloadingBench,
        Workshop,
        CookingStation,
    }

    public class Recipe
    {
        public string Name;
        public string Description;
        public CraftingStationType StationType;
        public List<RecipeIngredient> Inputs = new();
        public List<RecipeOutput> Outputs = new();
        public int RequiredSkillLevel;
        public SkillName RequiredSkill = SkillName.Repair;
        public float CraftTime = 2.0f;
    }

    public class RecipeIngredient
    {
        public string ItemName;
        public string ItemFormId;
        public int Count = 1;
        public bool Consumed = true;
    }

    public class RecipeOutput
    {
        public string ItemName;
        public string ItemFormId;
        public int Count = 1;
        public float ConditionRestore;
    }

    public partial class CraftingSystem : Node
    {
        private Dictionary<CraftingStationType, List<Recipe>> _recipes = new();
        private bool _isCrafting;
        private float _craftTimer;
        private Recipe _currentRecipe;

        [Signal]
        public delegate void CraftingStartedEventHandler(string recipeName, float duration);

        [Signal]
        public delegate void CraftingCompletedEventHandler(string recipeName, bool success);

        public bool IsCrafting => _isCrafting;

        public override void _Ready()
        {
            RegisterDefaultRecipes();
        }

        private void RegisterDefaultRecipes()
        {
            RegisterRecipe(new Recipe
            {
                Name = "Repair Weapon",
                Description = "Restore weapon condition using same weapon type",
                StationType = CraftingStationType.Workbench,
                Inputs = { new RecipeIngredient { ItemName = "Identical Weapon", Count = 1, Consumed = true } },
                Outputs = { new RecipeOutput { ItemName = "Repaired Weapon", Count = 1, ConditionRestore = 0.5f } },
                RequiredSkill = SkillName.Repair,
                RequiredSkillLevel = 25,
            });

            RegisterRecipe(new Recipe
            {
                Name = "Repair Armor",
                Description = "Restore armor condition",
                StationType = CraftingStationType.Workbench,
                Inputs = { new RecipeIngredient { ItemName = "Leather Belt", Count = 2 }, new RecipeIngredient { ItemName = "Wonderglue", Count = 1 } },
                Outputs = { new RecipeOutput { ItemName = "Repaired Armor", Count = 1, ConditionRestore = 0.25f } },
                RequiredSkill = SkillName.Repair,
                RequiredSkillLevel = 15,
            });

            RegisterRecipe(new Recipe
            {
                Name = "Stimpak",
                Description = "Craft a healing stimpak",
                StationType = CraftingStationType.ChemLab,
                Inputs = { new RecipeIngredient { ItemName = "Broken Stimpak", Count = 2 }, new RecipeIngredient { ItemName = "Medical Brace", Count = 1 }, new RecipeIngredient { ItemName = "Purified Water", Count = 1 } },
                Outputs = { new RecipeOutput { ItemName = "Stimpak", Count = 1 } },
                RequiredSkill = SkillName.Medicine,
                RequiredSkillLevel = 20,
            });

            RegisterRecipe(new Recipe
            {
                Name = "RadAway",
                Description = "Craft radiation remover",
                StationType = CraftingStationType.ChemLab,
                Inputs = { new RecipeIngredient { ItemName = "Radscorpion Poison Gland", Count = 2 }, new RecipeIngredient { ItemName = "Purified Water", Count = 2 } },
                Outputs = { new RecipeOutput { ItemName = "RadAway", Count = 1 } },
                RequiredSkill = SkillName.Medicine,
                RequiredSkillLevel = 30,
            });

            RegisterRecipe(new Recipe
            {
                Name = ".32 Round",
                Description = "Craft .32 caliber ammunition",
                StationType = CraftingStationType.ReloadingBench,
                Inputs = { new RecipeIngredient { ItemName = "Scrap Metal", Count = 2 }, new RecipeIngredient { ItemName = "Lead", Count = 1 } },
                Outputs = { new RecipeOutput { ItemName = ".32 Round", Count = 20 } },
                RequiredSkill = SkillName.Repair,
                RequiredSkillLevel = 10,
            });

            RegisterRecipe(new Recipe
            {
                Name = "5.56mm Round",
                Description = "Craft 5.56mm ammunition",
                StationType = CraftingStationType.ReloadingBench,
                Inputs = { new RecipeIngredient { ItemName = "Scrap Metal", Count = 3 }, new RecipeIngredient { ItemName = "Lead", Count = 2 }, new RecipeIngredient { ItemName = "Primer", Count = 1 } },
                Outputs = { new RecipeOutput { ItemName = "5.56mm Round", Count = 20 } },
                RequiredSkill = SkillName.Repair,
                RequiredSkillLevel = 25,
            });

            RegisterRecipe(new Recipe
            {
                Name = "Shotgun Shell",
                Description = "Craft 12 gauge shells",
                StationType = CraftingStationType.ReloadingBench,
                Inputs = { new RecipeIngredient { ItemName = "Scrap Metal", Count = 3 }, new RecipeIngredient { ItemName = "Lead", Count = 1 } },
                Outputs = { new RecipeOutput { ItemName = "Shotgun Shell", Count = 10 } },
                RequiredSkill = SkillName.Repair,
                RequiredSkillLevel = 15,
            });

            RegisterRecipe(new Recipe
            {
                Name = "Purified Water",
                Description = "Boil dirty water",
                StationType = CraftingStationType.CookingStation,
                Inputs = { new RecipeIngredient { ItemName = "Dirty Water", Count = 2 } },
                Outputs = { new RecipeOutput { ItemName = "Purified Water", Count = 1 } },
            });

            RegisterRecipe(new Recipe
            {
                Name = "Wood Shack",
                Description = "Build a small wooden shelter",
                StationType = CraftingStationType.Workshop,
                Inputs = { new RecipeIngredient { ItemName = "Wood", Count = 20 }, new RecipeIngredient { ItemName = "Scrap Metal", Count = 5 } },
                Outputs = { new RecipeOutput { ItemName = "Wood Shack", Count = 1 } },
            });

            RegisterRecipe(new Recipe
            {
                Name = "Water Purifier",
                Description = "Build a small water purifier",
                StationType = CraftingStationType.Workshop,
                Inputs = { new RecipeIngredient { ItemName = "Scrap Metal", Count = 10 }, new RecipeIngredient { ItemName = "Circuitry", Count = 3 }, new RecipeIngredient { ItemName = "Glass", Count = 5 } },
                Outputs = { new RecipeOutput { ItemName = "Water Purifier", Count = 1 } },
            });
        }

        public void RegisterRecipe(Recipe recipe)
        {
            if (!_recipes.ContainsKey(recipe.StationType))
                _recipes[recipe.StationType] = new List<Recipe>();
            _recipes[recipe.StationType].Add(recipe);
        }

        public List<Recipe> GetRecipesForStation(CraftingStationType station)
        {
            return _recipes.TryGetValue(station, out var list) ? new List<Recipe>(list) : new List<Recipe>();
        }

        public List<Recipe> GetAvailableRecipes(CraftingStationType station, int playerSkillLevel)
        {
            var all = GetRecipesForStation(station);
            return all.Where(r => playerSkillLevel >= r.RequiredSkillLevel).ToList();
        }

        public bool StartCrafting(Recipe recipe, int playerSkillLevel)
        {
            if (_isCrafting) return false;
            if (playerSkillLevel < recipe.RequiredSkillLevel) return false;

            if (!HasIngredients(recipe)) return false;

            _isCrafting = true;
            _currentRecipe = recipe;
            _craftTimer = recipe.CraftTime;

            EmitSignal(nameof(CraftingStartedEventHandler), recipe.Name, recipe.CraftTime);
            return true;
        }

        public override void _Process(double delta)
        {
            if (!_isCrafting || _currentRecipe == null) return;

            _craftTimer -= (float)delta;
            if (_craftTimer <= 0)
                CompleteCrafting();
        }

        private void CompleteCrafting()
        {
            if (_currentRecipe == null) return;

            foreach (var input in _currentRecipe.Inputs)
            {
                if (input.Consumed)
                    ConsumeItem(input.ItemName, input.Count);
            }

            foreach (var output in _currentRecipe.Outputs)
            {
                AddItem(output.ItemName, output.Count);
            }

            EmitSignal(nameof(CraftingCompletedEventHandler), _currentRecipe.Name, true);

            _isCrafting = false;
            _currentRecipe = null;
        }

        public void CancelCrafting()
        {
            _isCrafting = false;
            _currentRecipe = null;
            _craftTimer = 0;
        }

        private bool HasIngredients(Recipe recipe)
        {
            foreach (var input in recipe.Inputs)
            {
                if (!input.Consumed) continue;
                if (!CheckItem(input.ItemName, input.Count))
                    return false;
            }
            return true;
        }

        private bool CheckItem(string itemName, int count)
        {
            GD.Print($"[Crafting] Check '{itemName}' x{count} - TODO: connect to inventory");
            return true;
        }

        private void ConsumeItem(string itemName, int count)
        {
            GD.Print($"[Crafting] Consumed '{itemName}' x{count}");
        }

        private void AddItem(string itemName, int count)
        {
            GD.Print($"[Crafting] Added '{itemName}' x{count}");
        }
    }
}
