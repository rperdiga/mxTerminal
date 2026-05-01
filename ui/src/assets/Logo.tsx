
import React from 'react';
import { OneSourceSymbol } from './widgets/OneSourceSymbol';

interface LogoProps {
  className?: string;
  size?: number;
  showText?: boolean;
  // Configuration props
  tagline?: string;
  showTagline?: boolean;
  weightOne?: string;
  weightSource?: string;
  primaryColor?: string;
  secondaryColor?: string;
  gap?: string;
  font?: string;
}

export const OneSourceLogo: React.FC<LogoProps> = ({ 
  className = '', 
  size = 48, 
  showText = true,
  tagline = "Solutions Portal",
  showTagline = true,
  weightOne = "font-bold", 
  weightSource = "font-extrabold",
  primaryColor, 
  secondaryColor,
  gap = "gap-1",
  font = "manrope"
}) => {
  
  const getFontClass = () => {
    switch(font) {
      case 'inter': return 'font-sans';
      case 'tech': return 'font-tech'; // Space Grotesk
      case 'modern': return 'font-modern'; // Outfit
      default: return 'font-display'; // Manrope
    }
  };

  return (
    <div className={`flex items-center ${gap} ${className}`}>
      <OneSourceSymbol 
        size={size} 
        primaryColor={primaryColor} 
        secondaryColor={secondaryColor} 
      />
      
      {showText && (
        <div className={`flex flex-col justify-center ${!showTagline && 'h-full justify-center'}`}>
          <h1 className={`${getFontClass()} tracking-tight leading-none text-slate-800 dark:text-white ${!showTagline ? 'text-3xl' : 'text-2xl'}`}>
            <span className={weightOne}>One</span>
            <span className={`${weightSource} text-transparent bg-clip-text bg-gradient-to-r from-brand-primary to-brand-secondary`}>Source</span>
          </h1>
          {showTagline && (
            <span className="text-[10px] font-semibold tracking-widest uppercase text-slate-400 dark:text-slate-500 mt-0.5">
              {tagline}
            </span>
          )}
        </div>
      )}
    </div>
  );
};
