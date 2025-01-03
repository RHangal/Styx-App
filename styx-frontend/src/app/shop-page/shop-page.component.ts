import { Component, OnInit } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import { ShopService } from './shop.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-shop-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './shop-page.component.html',
  styleUrl: './shop-page.component.sass',
})
export class ShopPageComponent implements OnInit {
  auth0UserId: string | null = null;
  badges: any[] = [];
  errorMessage: string = '';
  successMessage: string = '';
  userCoins: string = '';
  loading: boolean = false;
  isTopUp = false; // Toggle state
  // Top-up options
  topUpOptions = [
    { price: 99.99, amount: 5000 },
    { price: 49.99, amount: 2500 },
    { price: 19.99, amount: 1000 },
    { price: 9.99, amount: 500 },
  ];

  toggleTopUp() {
    this.isTopUp = !this.isTopUp;
  }

  constructor(private shopService: ShopService, public auth: AuthService) {}

  ngOnInit(): void {
    this.auth.user$.subscribe((user) => {
      if (user && user.sub) {
        this.auth0UserId = user.sub; // Save auth0UserId for later use
        this.loadUserProfile();
      }
    });
    this.fetchBadges();
  }

  // Fetch badges from the server
  fetchBadges(): void {
    this.loading = true;
    this.shopService.getBadges().subscribe({
      next: (data) => {
        this.badges = data[0].badges; // Assuming the API returns an array of badges
        console.log(this.badges);
        this.loading = false;
      },
      error: (err) => {
        this.errorMessage = 'Failed to load badges. Please try again later.';
        this.loading = false;
      },
    });
  }
  // Purchase a badge
  purchaseBadge(value: number, imageUrl: string): void {
    if (!this.auth0UserId) {
      alert('User ID not found. Please log in.');
      return;
    }

    const confirmPurchase = window.confirm(
      `${value} coins will be spent, please confirm`
    );
    if (!confirmPurchase) {
      return; // Exit if the user cancels
    }

    console.log('Value being sent to the backend: ', value);
    this.shopService
      .purchaseBadges(this.auth0UserId, value, imageUrl)
      .subscribe({
        next: (response) => {
          console.log(response);
          if (response.Error) {
            alert(response.Error);
          } else {
            alert(response.Message);
            this.loadUserProfile();
            this.fetchBadges(); // Refresh badges list
          }
        },
        error: (err) => {
          alert('Failed to purchase badge. Please try again.');
        },
      });
  }

  loadUserProfile(): void {
    if (!this.auth0UserId) {
      console.error('User is not authenticated.');
      return;
    }
    this.shopService.getUserProfile(this.auth0UserId).subscribe(
      (data) => {
        this.userCoins = data.Coins;
      },
      (error) => {
        console.error('Error fetching profile:', error);
      }
    );
  }
}
