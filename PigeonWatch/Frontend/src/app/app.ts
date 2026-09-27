import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

interface WeatherForecast {
  date: string;
  temperatureC: number;
  summary: string;
}

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly forecasts = signal<WeatherForecast[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.http.get<WeatherForecast[]>('http://localhost:5285/weatherforecast').subscribe({
      next: (data) => this.forecasts.set(data),
      error: () => this.error.set('Could not reach the API.'),
    });
  }
}
