/*
 * Delta Forth - World's first Forth compiler for .NET
 * Copyright (C)1997-2019 Valer BOCAN, PhD, Romania (valer@bocan.ro)
 * 
 * This program and its source code is distributed in the hope that it will
 * be useful. No warranty of any kind is provided.
 * Please DO NOT distribute modified copies of the source code.
 * 
 */

using System;
using System.Collections;
using System.Text.RegularExpressions;
using DeltaForth.DataStructures;
using System.Collections.Generic;

namespace DeltaForth.SyntacticAnalyzer
{
	/// <summary>
	/// Delta Forth - The .NET Forth Compiler
	/// (C) Valer BOCAN, PhD (valer@bocan.ro)
	/// 
	/// Class ForthSyntacticAnalyzer
	/// 
	/// Date of creation:	    September 5, 2001
	/// Date of last update:	November 2, 2011
	/// 
	/// Description:
	/// </summary>
	
	internal class ForthSyntacticAnalyzer
	{
		private List<ForthVariable> GlobalVariables;	    // List of source global variables
		private List<ForthLocalVariable> LocalVariables;	// List of source local variables
		private List<ForthConstant> GlobalConstants;	    // List of source global constants
		private List<ForthWord> Words;      			    // List of words defined in the source file
		private List<ExternalWord> ExternalWords;	        // List of external words defined in the source file
		private string LibraryName;			                // Name of the library to be generated, null if an executable program should be generated

		private List<ForthAtom> SourceAtoms;		        // List of source atoms (obtained from a ForthParser)
		private Stack SourceStack;			                // Stack used when defining variables and constants
		private bool InWordDefinition;		                // TRUE if parsing inside a word
		private bool MainDefined;			                // TRUE if the word MAIN has been defined
	
		private string[] ReservedWords = new string[] {"@", "?", "!", "+!", "DUP", "-DUP", "DROP",
														"SWAP", "OVER", "ROT", ".", "+", "-", "*",
														"/", ">R", "R>", "I", "I'", "J", "MOD", "/MOD", "*/",
														"*/MOD", "MINUS", "ABS", "MIN", "MAX", "1+",
														"2+", "0=", "0<", "=", "<", ">", "<>", "AND",
														"OR", "XOR", "EMIT", "CR", "SPACE", "SPACES",
														"TYPE", "FILL", "ERASE", "BLANKS", "CMOVE", "KEY",
														"EXPECT", "PAD", "S0", "R0",
														"SP@", "SP!", "RP@", "RP!", "TIB", "QUERY",
														"STR2INT", "IF", "ELSE", "THEN", "DO", "LOOP",
														"+LOOP", "LEAVE", "BEGIN", "INT2STR",
														"AGAIN", "UNTIL", "WHILE", "REPEAT", "CASE", "OF", "ENDOF",
														"ENDCASE", "COUNT", "EXIT", "EXTERN", "CONSTANT",
														"VARIABLE", "ALLOT", "LIBRARY", "LOAD"};
		
		// ForthSyntacticAnalyzer constructor
		public ForthSyntacticAnalyzer(List<ForthAtom> p_SourceAtoms)
		{
			// Initialize variables
			GlobalVariables = new List<ForthVariable>();
            LocalVariables = new List<ForthLocalVariable>();
            GlobalConstants = new List<ForthConstant>();
			Words = new List<ForthWord>();
			ExternalWords = new List<ExternalWord>();
			SourceStack = new Stack();
			LibraryName = null;
			SourceAtoms = p_SourceAtoms;

			InWordDefinition = false;
			MainDefined = false;			
		}

        /// <summary>
        /// Get the meta information generated by the syntactic analyzer
        /// </summary>
        /// <returns>Compiler metadata.</returns>
        public CompilerMetadata GetMetaData()
		{
            // Analyze source atoms
            try
            {
                DoAnalysis();
            }
            catch (InvalidOperationException)
            {
                RaiseException(SyntacticExceptionType._EUnexpectedEndOfFile, ((ForthAtom)SourceAtoms[0]));
            }

            return new CompilerMetadata
            {                
                GlobalConstants = GlobalConstants,
                GlobalVariables = GlobalVariables,
                LocalVariables = LocalVariables,
                Words = Words,
                ExternalWords = ExternalWords,
                LibraryName = LibraryName
            };
		}

		// IsReserved - Checkes whether a specified word is reserved
		// Input:  WordName - the name of the atom
		// Output: Returns true if the specified word is reserved
		private bool IsReserved(string WordName)
		{
			bool reserved = false;
			WordName = WordName.ToLower();
			for(int i = 0; i < ReservedWords.Length; i++)
			{
				if(ReservedWords[i].ToLower() == WordName)
				{
					reserved = true;
					break;
				}
			}
			return reserved;

		}

		// IsConstOrVar - Checkes whether a specified word is a constant or a variable
		// Input:  Atom - the name of the atom
		// Output: Returns true if the specified word is a constant or a variable
		private bool IsConstOrVar(string Atom)
		{
			bool bConstOrVar = false;
			// Search constant name space
			for(int i = 0; i < GlobalConstants.Count; i++)
			{
				ForthConstant fc = (ForthConstant)GlobalConstants[i];
				if(fc.Name.ToUpper() == Atom.ToUpper()) bConstOrVar = true;
			}
			// Search variable name space
			for(int i = 0; i < GlobalVariables.Count; i++)
			{
				ForthVariable fv = (ForthVariable)GlobalVariables[i];
				if(fv.Name.ToUpper() == Atom.ToUpper()) bConstOrVar = true;
			}

			return bConstOrVar;
		}

		// IsIdentifier - Checkes whether a specified atom is a properly named identifier
		// Input:  Identifier - the name of the atom
		// Output: Returns true if the atom is an identifier
		private bool IsIdentifier(string atom)
		{
			if(atom.Length > 31) return false;						// The atom should not be longer than 31 characters
			if((atom[0] >= '0') && (atom[0] <= '9')) return false;	// The first character cannot be a figure
			if(IsReserved(atom)) return false;						// The atom should not be a reserved name
			return true;
		}

		// IsNumber - Checkes whether a specified atom is a number
		// Input:  Identifier - the name of the atom
		// Output: Returns true if the specified word is a number
		private bool IsNumber(string atom)
		{
			try 
			{
				Convert.ToInt32(atom);
			} 
			catch(Exception) 
			{
				return false;
			}
			return true;
		}

		// IsString - Checkes whether a specified atom is a string (should include the quotation marks)
		// Input:  Identifier - the name of the atom
		// Output: Returns true if the specified word is a string
		private bool IsString(string atom)
		{
			Regex reg = new Regex("\".*\"");	// Matches any string between quotation marks
			Match match = reg.Match(atom);
			return match.Success;
		}

		// DoAnalysis - Builds up the word, constant and variable lists
		// Input:  None
		// Output: None (Globally changes GlobalVariables, GlobalConstants, LocalVariables, Words)
		private void DoAnalysis()
		{
			ForthAtom Atom, NextAtom;
			ForthWord WordDef = null;	// Holds the definition of a word
			string temp;
			int noIFs = 0;		// Number of IF statements
			int noDOs = 0;		// Number of DO statements
			int noBEGINs = 0;	// Number of BEGIN statements
			int noWHILEs = 0;	// Number of WHILE statements
			int noCASEs = 0;	// Number of CASE statements
			int noOFs = 0;		// Number of OF statements

			// Define enumerator for the source atoms list
			IEnumerator saEnum = SourceAtoms.GetEnumerator();
			while(saEnum.MoveNext())
			{
				Atom = (ForthAtom)saEnum.Current;
				// If the atom does not start with " or ." make the atom upper case
				if(!Atom.Name.StartsWith(".\"") && !Atom.Name.StartsWith("\""))
					Atom.Name = Atom.Name.ToUpper();
				// Process atoms
				switch(Atom.Name)
				{
					case "EXTERN":	// Store information after EXTERN for calling methods at runtime
                        if (InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareOutsideWords, Atom);
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						string ForthWord = NextAtom.Name;		// Forth word name
                        if (IsReserved(ForthWord)) RaiseException(SyntacticExceptionType._EReservedWord, NextAtom);
                        if (!IsIdentifier(ForthWord)) RaiseException(SyntacticExceptionType._EInvalidIdentifier, NextAtom);
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						string FileName = NextAtom.Name;		// Library name
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						string Callee = NextAtom.Name;
						int DotPos = Callee.LastIndexOf('.');
						string ClassName = Callee.Substring(0, DotPos);
						string MethodName = Callee.Substring(DotPos + 1);
                        ExternalWords.Add(new ExternalWord { Name = ForthWord, Library = FileName, Class = ClassName, Method = MethodName });
						break;
					case "LIBRARY":	// Store next atom as the name of the library
                        if (InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareOutsideWords, Atom);
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						string libname = NextAtom.Name;
						if(IsReserved(libname)) RaiseException(SyntacticExceptionType._EReservedWord, NextAtom);
						if(!IsIdentifier(libname)) RaiseException(SyntacticExceptionType._EInvalidIdentifier, NextAtom);
						LibraryName = libname;
						break;
					case "CONSTANT": // Associate the value on the global stack to the next atom
						if(InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareOutsideWords, Atom);
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						if(IsReserved(NextAtom.Name)) RaiseException(SyntacticExceptionType._EReservedWord, NextAtom);
						if(!IsIdentifier(NextAtom.Name)) RaiseException(SyntacticExceptionType._EInvalidIdentifier, NextAtom);
						if(IsConstOrVar(NextAtom.Name)) RaiseException(SyntacticExceptionType._EDuplicateConst, NextAtom);
						if(SourceStack.Count == 0) RaiseException(SyntacticExceptionType._EUnableToDefineConst, NextAtom);
						GlobalConstants.Add(new ForthConstant{Name = NextAtom.Name, Value = SourceStack.Pop()}); // Add constant definiton to the list
						break;
					case "VARIABLE": // The next atom is the variable name with the initial size of 1
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						if(IsReserved(NextAtom.Name)) RaiseException(SyntacticExceptionType._EReservedWord, NextAtom);
						if(!IsIdentifier(NextAtom.Name)) RaiseException(SyntacticExceptionType._EInvalidIdentifier, NextAtom);
						if(IsConstOrVar(NextAtom.Name)) RaiseException(SyntacticExceptionType._EDuplicateVar, NextAtom);
						if(!InWordDefinition) 
						{
                            GlobalVariables.Add(new ForthVariable { Name = NextAtom.Name, Size = 1 });
						}
						else 
						{
                            LocalVariables.Add(new ForthLocalVariable { Name = NextAtom.Name, WordName = WordDef.Name });
						}
						break;
					case "ALLOT": // The last number on the stack is the additional size for the last defined variable
						if(InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareOutsideWords, Atom);
						if(SourceStack.Count == 0) RaiseException(SyntacticExceptionType._EUnableToAllocVar, Atom);			
						if(SourceStack.Peek().GetType() != typeof(int)) RaiseException(SyntacticExceptionType._EWrongAllotConstType, Atom);
						int AllotSize = (int)SourceStack.Pop();	// Get the ALLOT size
						// Change the size of the last defined variable
						int LastVarPos = GlobalVariables.Count - 1;	// Get the position of the last variable we defined
						ForthVariable fv = (ForthVariable)(GlobalVariables[LastVarPos]);
						fv.Size += AllotSize;
						GlobalVariables[LastVarPos] = fv;	// Update changes in list
						break;
					case ":": // Begin word definition
						if(InWordDefinition) RaiseException(SyntacticExceptionType._ENestedWordsNotAllowed, Atom);
						saEnum.MoveNext();	// Advance to next atom
						NextAtom = ((ForthAtom)saEnum.Current);
						NextAtom.Name = NextAtom.Name.ToUpper();
						if(IsReserved(NextAtom.Name)) RaiseException(SyntacticExceptionType._EReservedWord, NextAtom);
						if(!IsIdentifier(NextAtom.Name)) RaiseException(SyntacticExceptionType._EInvalidIdentifier, NextAtom);
						InWordDefinition = true;	// Signal beginning of word definition
						WordDef = new ForthWord{Name = NextAtom.Name};		// Alloc here the structure, we fill it later
						if(NextAtom.Name == "MAIN") MainDefined = true;
						break;
					case ";": // End word definition
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						// All control structures should be terminated by now
						if((noIFs > 0) || (noDOs > 0) || (noBEGINs > 0) || (noWHILEs > 0) || (noCASEs > 0))
							RaiseException(SyntacticExceptionType._EUnfinishedControlStruct, Atom);
						Words.Add(WordDef);	// Add word to the list
						InWordDefinition = false;	// Signal end of word definition
						break;
					case "IF": // IF statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						noIFs++;
						goto default;
					case "ELSE": // ELSE statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noIFs == 0) RaiseException(SyntacticExceptionType._EMalformedIETStruct, Atom);
						goto default;
					case "THEN": // THEN statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noIFs == 0) RaiseException(SyntacticExceptionType._EMalformedIETStruct, Atom);
						noIFs--;
						goto default;
					case "DO": // DO statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						noDOs++;
						goto default;
					case "LOOP": // LOOP statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noDOs == 0) RaiseException(SyntacticExceptionType._EMalformedDLStruct, Atom);
						noDOs--;
						goto default;
					case "+LOOP": // +LOOP statement
						goto case "LOOP";
					case "LEAVE": // LEAVE statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noDOs == 0) RaiseException(SyntacticExceptionType._EMalformedDLStruct, Atom);
						goto default;
					case "BEGIN": // BEGIN statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						noBEGINs++;
						goto default;
					case "WHILE": // WHILE statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noBEGINs == 0) RaiseException(SyntacticExceptionType._EMalformedBWRStruct, Atom);
						noWHILEs++;
						goto default;
					case "REPEAT": // REPEAT statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if((noBEGINs == 0) || (noWHILEs == 0)) RaiseException(SyntacticExceptionType._EMalformedBWRStruct, Atom);
						noBEGINs--;
						noWHILEs--;
						goto default;
					case "AGAIN": // AGAIN statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noBEGINs == 0) RaiseException(SyntacticExceptionType._EMalformedBAStruct, Atom);
						noBEGINs--;
						goto default;
					case "UNTIL": // UNTIL statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noBEGINs == 0) RaiseException(SyntacticExceptionType._EMalformedBUStruct, Atom);
						noBEGINs--;
						goto default;
					case "CASE": // CASE statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						noCASEs++;
						goto default;
					case "OF": // OF statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noCASEs == 0) RaiseException(SyntacticExceptionType._EMalformedCOEStruct, Atom);
						noOFs++;
						goto default;
					case "ENDOF": // ENDOF statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noOFs == 0) RaiseException(SyntacticExceptionType._EMalformedCOEStruct, Atom);
						noOFs--;
						goto default;
					case "ENDCASE": // ENDCASE statement
						if(!InWordDefinition) RaiseException(SyntacticExceptionType._EDeclareInsideWords, Atom);
						if(noOFs > 0) RaiseException(SyntacticExceptionType._EMalformedCOEStruct, Atom);
						noCASEs--;
						goto default;
					default: 
						temp = Atom.Name;	// Get the atom name
						if(InWordDefinition)
						{
							WordDef.Definition.Add(temp);	// Add atom to word definition
						}
						else 
						{
							// The atom should now be a constant previously defined, a number or a string
							int ConstValue;		// Here we hold the value of the constant, in case we find it
							if(GetConstIntValue(temp, out ConstValue)) SourceStack.Push(ConstValue);
							else if(IsNumber(temp)) SourceStack.Push(Convert.ToInt32(temp));
							else if(IsString(temp)) SourceStack.Push(temp.Trim(new char[] {'"'}));
							else RaiseException(SyntacticExceptionType._EInvalidIdentifier, Atom);
						}
						break;
				}
			}
			// Check whether word MAIN is defined
			if(MainDefined == false) RaiseException(SyntacticExceptionType._EMainNotDefined);
		}

		// GetConstIntValue - Retrieves the value of an integer constant (if found)
		// Input:  ConstName - the name of the constant
		// Output: ConstValue - The integer value of the constant
		private bool GetConstIntValue(string ConstName, out int ConstValue)
		{
			bool ConstFound = false;
			ConstValue = 0;

			IEnumerator ConstEnum = GlobalConstants.GetEnumerator();
			while(ConstEnum.MoveNext())
			{
				ForthConstant fc = (ForthConstant)ConstEnum.Current;
				if((fc.Name.ToLower() == ConstName.ToLower()) && (fc.Value.GetType() == typeof(int))) 
				{
					ConstValue = (int)fc.Value;
					ConstFound = true;
				}
			}
			return ConstFound;
		}

		// RaiseException - Throws a specified exception
		// Input:  SyntacticExceptionType - the code of the exception to be thrown
		//         ForthAtom - the atom (with file name and line number) that caused the error
		// Output: None
		// Overloads: 1
		private void RaiseException(SyntacticExceptionType exc)
		{
            ForthAtom atom = new ForthAtom { Name = string.Empty, FileName = string.Empty, LineNumber = 0 };	// Create an "empty" atom
			RaiseException(exc, atom);
		}

        private void RaiseException(SyntacticExceptionType exc, ForthAtom atom)
		{
			switch(exc) 
			{
				case SyntacticExceptionType._EDeclareOutsideWords:
					throw new Exception(atom.Name + " should be declared outside words. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EDeclareInsideWords:
					throw new Exception(atom.Name + " should be declared inside words. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EReservedWord:
					throw new Exception(atom.Name + " is a reserved identifier. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EInvalidIdentifier:
					throw new Exception(atom.Name + " is an invalid identifier. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EUnableToDefineConst:
					throw new Exception("Unable to define constant " + atom.Name + ". Number or string required before CONSTANT. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EUnableToAllocVar:
					throw new Exception("Unable to alloc variable space " + atom.Name + ". Number needed before ALLOT. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EUnexpectedEndOfFile:
					throw new Exception("Unexpected end of file " + atom.FileName + ".");
				case SyntacticExceptionType._EWrongAllotConstType:
					throw new Exception("Wrong constant type specified for ALLOT. Use an integer. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._ENestedWordsNotAllowed:
					throw new Exception("Nested words are not allowed. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMalformedBWRStruct:
					throw new Exception("Malformed BEGIN-WHILE-REPEAT control structure. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMalformedBAStruct:
					throw new Exception("Malformed BEGIN-AGAIN control structure. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMalformedBUStruct:
					throw new Exception("Malformed BEGIN-UNTIL control structure. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMalformedIETStruct:
					throw new Exception("Malformed IF-ELSE-THEN control structure. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMalformedCOEStruct:
					throw new Exception("Malformed CASE-OF-ENDCASE control structure. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EUnfinishedControlStruct:
					throw new Exception("Control structures must be terminated before ';'. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EMainNotDefined:
					throw new Exception("Program starting point is missing. Please define the word MAIN.");
				case SyntacticExceptionType._EDuplicateConst:
					throw new Exception("Constant redefines an already defined constant or variable. (" + atom.FileName + ":" + atom.LineNumber + ")");
				case SyntacticExceptionType._EDuplicateVar:
					throw new Exception("Variable redefines an already defined constant or variable. (" + atom.FileName + ":" + atom.LineNumber + ")");
			}
		}

	}	
}
