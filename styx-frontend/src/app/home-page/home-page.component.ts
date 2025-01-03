import { Component, OnInit } from '@angular/core';
import { CategoryService } from './home-page.service';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.sass',
})
export class HomePageComponent implements OnInit {
  categories: any[] = [];

  constructor(
    private categoryService: CategoryService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    this.categoryService.getCategories().subscribe({
      next: (data) => {
        this.categories = data;
      },
      error: (err) => {
        console.error('Error fetching categories', err);
      },
    });
  }

  navigateToPosts(postType: string): void {
    this.router.navigate(['/habits/', postType]);
  }
}
