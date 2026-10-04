export interface GenerateOptions {
  resxDir: string;
  outDir: string;
}

export interface GenerateResult {
  cultures: string[];
  keyCount: number;
}

export function parseResx(xml: string): Record<string, string>;

export function generate(options: GenerateOptions): GenerateResult;
