using Godot;
using System;
using KrgTerminal;


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property)]
public class TVarAttribute : Attribute
{
	public TVarAttribute()
	{

	}
}